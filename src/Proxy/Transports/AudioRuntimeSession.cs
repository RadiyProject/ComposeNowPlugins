using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using ComposeNowPlugins.Domain.Configurations;
using ComposeNowPlugins.Application.Exceptions;
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
    IPluginCatalog pluginCatalog,
    IPluginRepository pluginRepository,
    IPluginEventRepository pluginEventRepository,
    IPluginBlockProcessor pluginBlockProcessor
) : IRuntimeSession
{
    private readonly PluginId _pluginId = pluginId;
    private readonly string _pluginName = pluginName;
    private readonly ILogger<AudioRuntimeSession> _log = log;
    private readonly IPluginCatalog _pluginCatalog = pluginCatalog;
    private readonly IPluginRepository _pluginRepository = pluginRepository;
    private readonly IPluginEventRepository _pluginEventRepository = pluginEventRepository;
    private readonly IPluginBlockProcessor _pluginBlockProcessor = pluginBlockProcessor;

    private const int DefaultSampleRate = 44100;
    private const int DefaultBlockSize = 512;
    private const int DefaultChannels = 2;
    private const string DefaultMode = "realtime";

    private const int RenderInitialPrefillBlocks = 0;

    private readonly PluginCacheLeaseRenewer _pluginCacheLeaseRenewer = new(pluginId, pluginRepository, log);
    private readonly PluginEventCommandHandler _pluginEventCommandHandler = new(pluginId, pluginEventRepository);
    private readonly AudioOutputWriter _audioOutputWriter = new();

    private long _lastProcessedSeq = -1;
    private volatile bool _offlineMode;
    private readonly ConcurrentDictionary<ulong, int> _offlineBlockFrames = new();
    private readonly ConcurrentDictionary<ulong, AudioInputBlock> _inputBlocks = new();

    private TaskCompletionSource<ulong>? _renderReadyTcs;
    private ulong _currentEpoch;

    public async Task RunAsync(
        IRuntimeChannel ch,
        CancellationToken ct
    )
    {
        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        CancellationToken sessionCt = sessionCts.Token;

        Plugin plugin = await EnsurePluginExistsAsync();

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

        int credits = 0;
        using var creditSignal = new SemaphoreSlim(0, int.MaxValue);

        Task reader = Task.Run(
            async () =>
            {
                await ReadInputLoopAsync(
                    ch,
                    helloTcs,
                    creditSignal,
                    blocks => Interlocked.Add(ref credits, blocks),
                    sessionCt
                );
            },
            sessionCt
        );

        Task pluginCacheLease = Task.Run(
            () => _pluginCacheLeaseRenewer.RunAsync(
                ch,
                sessionCts
            ),
            sessionCt
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
            _offlineBlockFrames.Clear();
            _inputBlocks.Clear();

            plugin.SetAudioConfiguration(
                sampleRate,
                blockSize,
                channels
            );

            await _pluginRepository.UpdateAsync(
                _pluginId,
                plugin
            );

            await RunProcessingLoopAsync(
                ch,
                sampleRate,
                blockSize,
                channels,
                offline,
                plugin.Descriptor.Type == PluginType.EFFECT,
                () => Volatile.Read(ref credits),
                () => Interlocked.Decrement(ref credits),
                creditSignal,
                sessionCt
            );
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
                // Нормальное завершение при закрытии соединения.
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
                // Нормальное завершение при закрытии соединения.
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

    private async Task<Plugin> EnsurePluginExistsAsync()
    {
        Plugin? existing = await _pluginRepository.GetAsync(_pluginId);

        if (existing is not null)
        {
            return existing;
        }

        var descriptor = _pluginCatalog.GetRequired(_pluginName)
            ?? throw new InvalidOperationException(
                $"Plugin '{_pluginName}' is not registered or disabled."
            );

        Plugin plugin = new(
            _pluginId,
            descriptor
        );

        try
        {
            await _pluginRepository.AddAsync(plugin);

            _log.LogDebug(
                "Plugin state created. PluginId={PluginId}, PluginName={PluginName}",
                _pluginId,
                _pluginName
            );

            return plugin;
        }
        catch (EntityAlreadyExistsException)
        {
        }

        Plugin? createdByAnotherRequest = await _pluginRepository.GetAsync(_pluginId);

        if (createdByAnotherRequest is not null)
        {
            return createdByAnotherRequest;
        }

        throw new InvalidOperationException(
            $"Failed to create plugin state. PluginId={_pluginId}, PluginName={_pluginName}"
        );
    }

    private async Task ReadInputLoopAsync(
        IRuntimeChannel ch,
        TaskCompletionSource<HelloOptions> helloTcs,
        SemaphoreSlim creditSignal,
        Action<int> addCredits,
        CancellationToken ct
    )
    {
        await foreach (IncomingMessage msg in ch.ReadAllAsync(ct))
        {
            ReadOnlyMemory<byte> payload = msg.Payload;
            ReadOnlySpan<byte> span = payload.Span;

            if (msg.ContentType == "text/plain")
            {
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
                addCredits,
                creditSignal
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
                if ((long)audioSeq <= Volatile.Read(ref _lastProcessedSeq))
                {
                    _log.LogDebug(
                        "Late AIN1 input block dropped. PluginId={PluginId}, InputSeq={InputSeq}, LastProcessedSeq={LastProcessedSeq}",
                        _pluginId.GetValue(),
                        audioSeq,
                        Volatile.Read(ref _lastProcessedSeq)
                    );
                    continue;
                }

                _inputBlocks[audioSeq] = inputBlock;

                if (_offlineMode)
                {
                    _offlineBlockFrames[audioSeq] = inputBlock.Frames;
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

        if (offline && !isLate)
        {
            _offlineBlockFrames[seq] = blockFrames;
        }

        foreach (PluginEventInput item in events)
        {
            PluginEvent pluginEvent = item.Type == 2
                ? PluginEvent.NoteOff(
                    _pluginId,
                    item.Pitch,
                    seq,
                    item.Offset,
                    blockFrames
                )
                : PluginEvent.NoteOn(
                    _pluginId,
                    item.Pitch,
                    item.Velocity,
                    seq,
                    item.Offset,
                    blockFrames
                );

            if (isLate)
            {
                if (offline)
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

                _log.LogWarning(
                    "Late realtime EVT1 event. Applying as control event. PluginId={PluginId}, EventSeq={EventSeq}, LastProcessedSeq={LastProcessedSeq}, Type={Type}, Pitch={Pitch}",
                    _pluginId.GetValue(),
                    seq,
                    lastProcessedSeq,
                    item.Type,
                    item.Pitch
                );

                await _pluginEventRepository.AddControlEventAsync(
                    _pluginId,
                    pluginEvent
                );

                continue;
            }

            await _pluginEventRepository.AddBlockEventAsync(
                _pluginId,
                seq,
                pluginEvent
            );
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
        Func<int> getCredits,
        Action decrementCredits,
        SemaphoreSlim creditSignal,
        CancellationToken ct
    )
    {
        sampleRate = NormalizePositive(sampleRate, DefaultSampleRate);
        blockSize = NormalizePositive(blockSize, DefaultBlockSize);
        channels = NormalizePositive(channels, DefaultChannels);

        TimeSpan period = TimeSpan.FromSeconds(
            (double)blockSize / sampleRate
        );

        long next = Stopwatch.GetTimestamp();
        double freq = Stopwatch.Frequency;

        ulong seq = 0;
        ulong ts = 0;

        _currentEpoch = CreateEpoch();

        _renderReadyTcs = new TaskCompletionSource<ulong>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        string beginMessage = offline
            ? $"render begin {_currentEpoch} 0 {RenderInitialPrefillBlocks}"
            : $"realtime begin {_currentEpoch} 0";

        await ch.SendAsync(
            Encoding.UTF8.GetBytes(beginMessage),
            "text/plain",
            true,
            ct
        );

        _log.LogDebug(
            "Audio session begin sent. PluginId={PluginId}, Epoch={Epoch}, Mode={Mode}, SampleRate={SampleRate}, BlockSize={BlockSize}, Channels={Channels}",
            _pluginId.GetValue(),
            _currentEpoch,
            offline ? "offline" : "realtime",
            sampleRate,
            blockSize,
            channels
        );

        TimeSpan readyTimeout = offline
            ? TimeSpan.FromSeconds(2)
            : TimeSpan.FromMilliseconds(100);

        bool readyConfirmed = false;
        try
        {
            await _renderReadyTcs.Task.WaitAsync(
                readyTimeout,
                ct
            );

            readyConfirmed = true;
        }
        catch (TimeoutException)
        {
            readyConfirmed = false;
        }

        if (readyConfirmed)
        {
            _log.LogDebug(
                "Audio session ready confirmed. PluginId={PluginId}, Epoch={Epoch}, Mode={Mode}",
                _pluginId.GetValue(),
                _currentEpoch,
                offline ? "offline" : "realtime"
            );
        }
        else if (offline)
        {
            _log.LogWarning(
                "Offline audio session ready confirmation timeout. Continuing may produce broken render. PluginId={PluginId}, Epoch={Epoch}",
                _pluginId.GetValue(),
                _currentEpoch
            );
        }
        else
        {
            _log.LogDebug(
                "Realtime ready confirmation timeout. Continuing. PluginId={PluginId}, Epoch={Epoch}",
                _pluginId.GetValue(),
                _currentEpoch
            );
        }

        ulong epoch = _currentEpoch;

        while (!ct.IsCancellationRequested)
        {
            if (offline)
            {
                while (getCredits() <= 0)
                {
                    await creditSignal.WaitAsync(ct);
                }
            }

            int framesForBlock = blockSize;

            if (offline &&
                !_offlineBlockFrames.TryRemove(seq, out framesForBlock))
            {
                framesForBlock = blockSize;

                _log.LogWarning(
                    "Offline block size marker was not found. Falling back to configured block size. PluginId={PluginId}, Seq={Seq}, BlockSize={BlockSize}",
                    _pluginId.GetValue(),
                    seq,
                    blockSize
                );
            }

            framesForBlock = NormalizePositive(framesForBlock, blockSize);

            AudioInputBlock? inputBlock = null;
            if (waitForInputAudio)
            {
                long inputDeadline = offline
                    ? Stopwatch.GetTimestamp() + (long)(TimeSpan.FromSeconds(10).TotalSeconds * Stopwatch.Frequency)
                    : Stopwatch.GetTimestamp() + (long)(TimeSpan.FromMilliseconds(250).TotalSeconds * Stopwatch.Frequency);

                while (!_inputBlocks.TryRemove(seq, out inputBlock))
                {
                    if (Stopwatch.GetTimestamp() >= inputDeadline)
                    {
                        break;
                    }

                    await Task.Delay(1, ct);
                }

                if (inputBlock is null && !offline)
                {
                    _log.LogDebug(
                        "Realtime AIN1 input block timeout. Processing silence to keep effect stream alive. PluginId={PluginId}, Seq={Seq}, BlockSize={BlockSize}, Channels={Channels}",
                        _pluginId.GetValue(),
                        seq,
                        blockSize,
                        channels
                    );
                }
            }
            else
            {
                _inputBlocks.TryRemove(seq, out inputBlock);
            }

            if (inputBlock is not null)
            {
                framesForBlock = NormalizePositive(inputBlock.Frames, framesForBlock);
            }

            PluginBlockProcessResult result = await _pluginBlockProcessor.ProcessBlockAsync(
                _pluginId,
                seq,
                framesForBlock,
                offline,
                inputBlock is not null
                    ? new ReadOnlyMemory<float>(inputBlock.Audio)
                    : null,
                ct
            );

            try
            {
                Volatile.Write(ref _lastProcessedSeq, (long)seq);

                if (result.ShouldSend)
                {
                    await _audioOutputWriter.WriteAsync(
                        ch,
                        epoch,
                        seq,
                        ts,
                        sampleRate,
                        channels,
                        framesForBlock,
                        result.Audio,
                        ct
                    );
                }
            }
            finally
            {
                result.Dispose();
            }

            if (offline)
            {
                decrementCredits();
            }

            seq++;
            ts += (ulong)framesForBlock;

            if (!offline && !waitForInputAudio)
            {
                long now = Stopwatch.GetTimestamp();
                long periodTicks = (long)(period.TotalSeconds * freq);

                next += periodTicks;

                long lagTicks = now - next;

                if (lagTicks > periodTicks * 2)
                {
                    next = now + periodTicks;

                    _log.LogDebug(
                        "Realtime processing lag corrected. PluginId={PluginId}, Seq={Seq}, LagMs={LagMs:F2}",
                        _pluginId.GetValue(),
                        seq,
                        lagTicks * 1000.0 / freq
                    );
                }

                TimeSpan delay = TimeSpan.FromSeconds(
                    (next - Stopwatch.GetTimestamp()) / freq
                );

                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, ct);
                }
            }
        }
    }

    private static int NormalizePositive(
        int value,
        int fallback
    )
    {
        return value > 0 ? value : fallback;
    }

    private static ulong CreateEpoch()
    {
        long value = Random.Shared.NextInt64(
            1,
            long.MaxValue
        );

        return unchecked((ulong)value);
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
