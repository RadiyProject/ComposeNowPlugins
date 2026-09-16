using System.Text;
using ComposeNowPlugins.Domain.Configurations;
using ComposeNowPlugins.Domain.Models;
using ComposeNowPlugins.Domain.Models.Ids;
using ComposeNowPlugins.Application.Repositories.Plugins;
using ComposeNowPlugins.Application.Services.Plugins;
using ComposeNowPlugins.Application.Services.Processing;

namespace ComposeNowPlugins.Proxy.Transports;

public sealed class AudioRuntimeSession(
    PluginId pluginId,
    string pluginName,
    ILogger<AudioRuntimeSession> log,
    IPluginRepository pluginRepository,
    IPluginSessionService pluginSessionService,
    IPluginEventService pluginEventService,
    IAudioSessionCoordinator audioSessionCoordinator
) : IRuntimeSession
{
    private readonly PluginId _pluginId = pluginId;
    private readonly string _pluginName = pluginName;
    private readonly ILogger<AudioRuntimeSession> _log = log;
    private readonly IPluginRepository _pluginRepository = pluginRepository;
    private readonly IPluginSessionService _pluginSessionService = pluginSessionService;
    private readonly IPluginEventService _pluginEventService = pluginEventService;
    private readonly IAudioSessionCoordinator _audioSessionCoordinator = audioSessionCoordinator;

    private const int DefaultSampleRate = 44100;
    private const int DefaultBlockSize = 512;
    private const int DefaultChannels = 2;
    private const string DefaultMode = "realtime";

    private readonly PluginCacheLeaseRenewer _pluginCacheLeaseRenewer = new(pluginId, pluginRepository, log);
    private readonly PluginEventCommandHandler _pluginEventCommandHandler = new(
        pluginId,
        pluginEventService
    );
    private long _lastProcessedSeq = -1;
    private volatile bool _offlineMode;
    private readonly SequencedBuffer<int> _offlineBlockFrames = new(
        AudioProtocolLimits.MaxBufferedBlocks,
        AudioProtocolLimits.MaxFutureSequenceDistance
    );
    private readonly SequencedBuffer<AudioInputBlock> _inputBlocks = new(
        AudioProtocolLimits.MaxBufferedBlocks,
        AudioProtocolLimits.MaxFutureSequenceDistance
    );

    private TaskCompletionSource<ulong>? _renderReadyTcs;
    private ulong _currentEpoch;

    public async Task RunAsync(
        IRuntimeChannel ch,
        CancellationToken ct
    )
    {
        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        CancellationToken sessionCt = sessionCts.Token;

        Plugin plugin = await _pluginSessionService.GetOrCreateAsync(
            _pluginId,
            _pluginName
        );
        _currentEpoch = CreateEpoch();

        await ch.SendAsync(
            Encoding.UTF8.GetBytes($"plugin id {_pluginId.GetValue()}"),
            "text/plain",
            true,
            sessionCt
        );

        int sampleRate = NormalizePositive(plugin.SampleRate, DefaultSampleRate);
        int blockSize = NormalizePositive(plugin.BlockSize, DefaultBlockSize);
        int channels = NormalizePositive(plugin.Channels, DefaultChannels);
        string mode = DefaultMode;

        var helloTcs = new TaskCompletionSource<HelloOptions>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        using var creditSignal = new SemaphoreSlim(
            0,
            AudioProtocolLimits.MaxOutstandingCredits
        );

        Task reader = ReadInputLoopAsync(
            ch,
            helloTcs,
            creditSignal,
            sessionCt
        );

        Task pluginCacheLease = _pluginCacheLeaseRenewer.RunAsync(
            ch,
            sessionCts
        );

        try
        {
            HelloOptions? hello = await WaitHelloAsync(
                helloTcs,
                sessionCt
            );

            if (hello is not null)
            {
                sampleRate = NormalizePositive(hello.SampleRate, DefaultSampleRate);
                blockSize = NormalizePositive(hello.BlockSize, DefaultBlockSize);
                channels = NormalizePositive(hello.Channels, DefaultChannels);
                mode = string.IsNullOrWhiteSpace(hello.Mode)
                    ? DefaultMode
                    : hello.Mode;

                _log.LogDebug(
                    "Hello received. PluginId={PluginId}, SampleRate={SampleRate}, BlockSize={BlockSize}, Channels={Channels}, Mode={Mode}",
                    _pluginId,
                    sampleRate,
                    blockSize,
                    channels,
                    mode
                );
            }
            else
            {
                _log.LogWarning(
                    "Hello was not received. PluginId={PluginId}. Using cached/default audio configuration. SampleRate={SampleRate}, BlockSize={BlockSize}, Channels={Channels}, Mode={Mode}",
                    _pluginId,
                    sampleRate,
                    blockSize,
                    channels,
                    mode
                );
            }

            bool offline = string.Equals(
                mode,
                "offline",
                StringComparison.OrdinalIgnoreCase
            );

            _offlineMode = offline;
            Volatile.Write(ref _lastProcessedSeq, -1);

            plugin.SampleRate = sampleRate;
            plugin.BlockSize = blockSize;
            plugin.Channels = channels;

            await _pluginRepository.UpdateAsync(
                _pluginId,
                plugin
            );

            Task processingLoop = RunProcessingLoopAsync(
                ch,
                sampleRate,
                blockSize,
                channels,
                offline,
                plugin.Descriptor.Type == PluginType.EFFECT,
                creditSignal,
                sessionCt
            );

            Task completed = await Task.WhenAny(reader, processingLoop);
            if (completed == reader)
            {
                await sessionCts.CancelAsync();
                try
                {
                    await reader;
                }
                finally
                {
                    try
                    {
                        await processingLoop;
                    }
                    catch (OperationCanceledException) when (sessionCt.IsCancellationRequested)
                    {
                    }
                }

                return;
            }

            await processingLoop;
        }
        finally
        {
            await sessionCts.CancelAsync();

            try
            {
                await reader;
            }
            catch (OperationCanceledException)
            {
                // Normal completion when the connection closes.
            }
            catch (System.Net.WebSockets.WebSocketException exception)
            {
                _log.LogDebug(
                    exception,
                    "Audio input reader websocket closed. PluginId={PluginId}",
                    _pluginId
                );
            }
            catch (Exception exception)
            {
                _log.LogWarning(
                    exception,
                    "Audio input reader failed. PluginId={PluginId}",
                    _pluginId
                );
            }

            try
            {
                await pluginCacheLease;
            }
            catch (OperationCanceledException)
            {
                // Normal completion when the connection closes.
            }
            catch (Exception exception)
            {
                _log.LogWarning(
                    exception,
                    "Plugin cache lease loop failed. PluginId={PluginId}",
                    _pluginId
                );
            }
        }
    }

    private async Task<HelloOptions?> WaitHelloAsync(
        TaskCompletionSource<HelloOptions> helloTcs,
        CancellationToken ct
    )
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2));

            return await helloTcs.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _log.LogDebug(
                "Hello was not received in time. PluginId={PluginId}",
                _pluginId
            );

            return null;
        }
    }

    private async Task ReadInputLoopAsync(
        IRuntimeChannel ch,
        TaskCompletionSource<HelloOptions> helloTcs,
        SemaphoreSlim creditSignal,
        CancellationToken ct
    )
    {
        await foreach (IncomingMessage msg in ch.ReadAllAsync(ct))
        {
            ReadOnlyMemory<byte> payload = msg.Payload;
            ReadOnlySpan<byte> span = payload.Span;

            if (msg.ContentType == "text/plain")
            {
                if (payload.Length > AudioProtocolLimits.MaxTextMessageBytes)
                {
                    throw new MessageTooLargeException(
                        $"Text message exceeds {AudioProtocolLimits.MaxTextMessageBytes} bytes."
                    );
                }

                string text = AudioProtocolReader.ReadText(span);

                await HandleTextMessageAsync(
                    text,
                    helloTcs,
                    ct
                );

                continue;
            }

            if (AudioProtocolReader.TryHandleCreditMessage(
                span,
                creditSignal,
                _offlineMode
            ))
            {
                continue;
            }

            if (AudioProtocolReader.TryParseEvt1Message(
                span,
                out ulong evtSeq,
                out int blockFrames,
                out List<PluginEventInput> evtList
            ))
            {
                await SaveEvt1EventsAsync(
                    evtSeq,
                    blockFrames,
                    evtList,
                    _offlineMode
                );

                continue;
            }

            if (AudioProtocolReader.TryParseAudioInputMessage(
                span,
                out ulong audioSeq,
                out AudioInputBlock? inputBlock
            ) && inputBlock is not null)
            {
                long lastProcessedSequence = Volatile.Read(ref _lastProcessedSeq);
                if (lastProcessedSequence >= 0 && audioSeq <= (ulong)lastProcessedSequence)
                {
                    _log.LogDebug(
                        "Late AIN1 input block dropped. PluginId={PluginId}, InputSeq={InputSeq}, LastProcessedSeq={LastProcessedSeq}",
                        _pluginId.GetValue(),
                        audioSeq,
                        Volatile.Read(ref _lastProcessedSeq)
                    );
                    continue;
                }

                if (!_inputBlocks.TryAdd(
                    audioSeq,
                    inputBlock,
                    Volatile.Read(ref _lastProcessedSeq)
                ))
                {
                    throw new MessageTooLargeException(
                        $"Audio input sequence buffer limit exceeded. Seq={audioSeq}."
                    );
                }

                if (_offlineMode)
                {
                    AddOfflineBlockFrames(audioSeq, inputBlock.Frames);
                }

                continue;
            }

            _log.LogDebug(
                "Unknown binary message. PluginId={PluginId}, Length={Length}",
                _pluginId,
                span.Length
            );
        }
    }

    private async Task SaveEvt1EventsAsync(
        ulong seq,
        int blockFrames,
        List<PluginEventInput> events,
        bool offline
    )
    {
        long lastProcessedSeq = Volatile.Read(ref _lastProcessedSeq);
        bool isLate = lastProcessedSeq >= 0 && seq <= (ulong)lastProcessedSeq;

        if (!isLate && IsTooFarInFuture(seq, lastProcessedSeq))
        {
            throw new MessageTooLargeException(
                $"Plugin event sequence exceeds the future window. Seq={seq}."
            );
        }

        if (offline && !isLate)
        {
            AddOfflineBlockFrames(seq, blockFrames);
        }

        foreach (PluginEventInput item in events)
        {
            PluginEventDispatchResult dispatchResult = await _pluginEventService.DispatchBlockNoteAsync(
                _pluginId,
                _currentEpoch,
                seq,
                blockFrames,
                new PluginNoteEventCommand(
                    item.Pitch,
                    item.Velocity,
                    item.Offset,
                    IsNoteOff: item.Type == 2
                ),
                isLate,
                offline
            );

            if (dispatchResult == PluginEventDispatchResult.Dropped)
            {
                _log.LogWarning(
                    "Late offline EVT1 event dropped. PluginId={PluginId}, EventSeq={EventSeq}, LastProcessedSeq={LastProcessedSeq}, Type={Type}, Pitch={Pitch}",
                    _pluginId.GetValue(),
                    seq,
                    lastProcessedSeq,
                    item.Type,
                    item.Pitch
                );

                continue;
            }

            if (dispatchResult == PluginEventDispatchResult.ControlQueue)
            {
                _log.LogWarning(
                    "Late realtime EVT1 event. Applying as control event. PluginId={PluginId}, EventSeq={EventSeq}, LastProcessedSeq={LastProcessedSeq}, Type={Type}, Pitch={Pitch}",
                    _pluginId.GetValue(),
                    seq,
                    lastProcessedSeq,
                    item.Type,
                    item.Pitch
                );
            }
        }
    }

    private async Task HandleTextMessageAsync(
        string text,
        TaskCompletionSource<HelloOptions> helloTcs,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (_pluginCacheLeaseRenewer.TryHandleConfirmation(text))
        {
            return;
        }

        if (TryHandleAudioReady(text))
        {
            return;
        }

        if (AudioProtocolReader.TryParseHello(
            text,
            out HelloOptions? hello
        ))
        {
            if (hello is not null)
            {
                _offlineMode = string.Equals(
                    hello.Mode,
                    "offline",
                    StringComparison.OrdinalIgnoreCase
                );
            }

            helloTcs.TrySetResult(hello!);
            return;
        }

        if (await _pluginEventCommandHandler.TryHandleAsync(text))
        {
            return;
        }

        _log.LogDebug(
            "Unknown text message. PluginId={PluginId}, Text={Text}",
            _pluginId,
            text
        );
    }

    private async Task RunProcessingLoopAsync(
        IRuntimeChannel ch,
        int sampleRate,
        int blockSize,
        int channels,
        bool offline,
        bool waitForInputAudio,
        SemaphoreSlim creditSignal,
        CancellationToken ct
    )
    {
        sampleRate = NormalizePositive(sampleRate, DefaultSampleRate);
        blockSize = NormalizePositive(blockSize, DefaultBlockSize);
        channels = NormalizePositive(channels, DefaultChannels);

        _renderReadyTcs = new TaskCompletionSource<ulong>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        var configuration = new AudioSessionConfiguration(
            _pluginId,
            _currentEpoch,
            sampleRate,
            blockSize,
            channels,
            offline,
            waitForInputAudio
        );
        var port = new ProxyAudioSessionPort(
            _pluginId,
            configuration,
            ch,
            new AudioOutputWriter(),
            _offlineBlockFrames,
            _inputBlocks,
            _renderReadyTcs,
            creditSignal,
            sequence => Volatile.Write(
                ref _lastProcessedSeq,
                unchecked((long)sequence)
            ),
            _log
        );

        await _audioSessionCoordinator.RunAsync(
            configuration,
            port,
            ct
        );
    }

    private static int NormalizePositive(
        int value,
        int fallback
    )
    {
        return value > 0 ? value : fallback;
    }

    private void AddOfflineBlockFrames(ulong sequence, int frames)
    {
        if (!_offlineBlockFrames.TryAdd(
            sequence,
            frames,
            Volatile.Read(ref _lastProcessedSeq)
        ))
        {
            throw new MessageTooLargeException(
                $"Offline frame marker buffer limit exceeded. Seq={sequence}."
            );
        }
    }

    private static ulong CreateEpoch()
    {
        long value = Random.Shared.NextInt64(
            1,
            long.MaxValue
        );

        return unchecked((ulong)value);
    }

    private static bool IsTooFarInFuture(ulong sequence, long lastProcessedSequence)
    {
        ulong baseline = lastProcessedSequence < 0
            ? 0
            : (ulong)lastProcessedSequence;
        ulong maxDistance = AudioProtocolLimits.MaxFutureSequenceDistance;
        ulong maxAllowed = ulong.MaxValue - baseline < maxDistance
            ? ulong.MaxValue
            : baseline + maxDistance;

        return sequence > maxAllowed;
    }

    private bool TryHandleAudioReady(string text)
    {
        const string renderPrefix = "render ready ";
        const string realtimePrefix = "realtime ready ";

        string? epochText;
        if (text.StartsWith(renderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            epochText = text[renderPrefix.Length..].Trim();
        }
        else if (text.StartsWith(realtimePrefix, StringComparison.OrdinalIgnoreCase))
        {
            epochText = text[realtimePrefix.Length..].Trim();
        }
        else
        {
            return false;
        }

        if (!ulong.TryParse(epochText, out ulong epoch))
        {
            return true;
        }

        if (epoch == _currentEpoch)
        {
            _renderReadyTcs?.TrySetResult(epoch);
        }

        return true;
    }
}
