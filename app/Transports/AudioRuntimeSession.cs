using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using ComposeNowPlugins.Models;
using ComposeNowPlugins.Models.Ids;
using ComposeNowPlugins.Repositories;
using ComposeNowPlugins.Repositories.Plugins;
using ComposeNowPlugins.Services.Plugins;
using ComposeNowPlugins.Services.Processing;

namespace ComposeNowPlugins.Transports;

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

    private const double RealtimeDefaultLatencySeconds = 0.10;
    private const double OfflineRenderLatencySeconds = 1.00;
    private const int RenderDelayBlocks = 8;
    private const int RenderInitialPrefillBlocks = 0;

    private record Evt(int Type, int Pitch, float Vel, int Offs);

    private static readonly TimeSpan PluginCacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PluginCacheRenewBefore = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PluginCacheRenewResponseTimeout = TimeSpan.FromSeconds(10);

    private readonly Lock _pluginCacheRenewLock = new();

    private string? _expectedPluginCacheRenewToken;
    private TaskCompletionSource<string>? _pluginCacheRenewTcs;

    private long _lastProcessedSeq = -1;
    private volatile bool _offlineMode;
    private readonly ConcurrentDictionary<ulong, int> _offlineBlockFrames = new();

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
            () => RunPluginCacheLeaseLoopAsync(
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

                _log.LogInformation(
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

        RepositoryActionStatus status = await _pluginRepository.AddAsync(plugin);

        if (status == RepositoryActionStatus.Success)
        {
            _log.LogInformation(
                "Plugin state created. PluginId={PluginId}, PluginName={PluginName}",
                _pluginId,
                _pluginName
            );

            return plugin;
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
                string text = Encoding.UTF8.GetString(span).Trim();

                await HandleTextMessageAsync(
                    text,
                    helloTcs,
                    ct
                );

                continue;
            }

            if (TryHandleCreditMessage(
                span,
                addCredits,
                creditSignal
            ))
            {
                continue;
            }

            if (TryParseEvt1Message(
                span,
                out ulong evtSeq,
                out int blockFrames,
                out List<Evt> evtList
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
        List<Evt> events,
        bool offline
    )
    {
        long lastProcessedSeq = Volatile.Read(ref _lastProcessedSeq);
        bool isLate = lastProcessedSeq >= 0 && seq <= (ulong)lastProcessedSeq;

        if (offline && !isLate)
        {
            _offlineBlockFrames[seq] = blockFrames;
        }

        foreach (Evt item in events)
        {
            PluginEvent pluginEvent = item.Type == 2
                ? PluginEvent.NoteOff(
                    _pluginId,
                    item.Pitch,
                    seq,
                    item.Offs,
                    blockFrames
                )
                : PluginEvent.NoteOn(
                    _pluginId,
                    item.Pitch,
                    item.Vel,
                    seq,
                    item.Offs,
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

        if (TryHandlePluginCacheRenewConfirmation(text))
        {
            return;
        }

        if (TryHandleAudioReady(text))
        {
            return;
        }

        if (TryParseHello(
            text,
            out HelloOptions? hello
        ))
        {
            helloTcs.TrySetResult(hello!);
            return;
        }

        if (await TryHandleNoteOnAsync(text))
        {
            return;
        }

        if (await TryHandleNoteOffAsync(text))
        {
            return;
        }

        if (await TryHandleParamAsync(text))
        {
            return;
        }

        if (await TryHandlePanicAsync(text))
        {
            return;
        }

        _log.LogDebug(
            "Unknown text message. PluginId={PluginId}, Text={Text}",
            _pluginId,
            text
        );
    }

    private static bool TryParseHello(
        string text,
        out HelloOptions? hello
    )
    {
        hello = null;

        if (!text.StartsWith("hello ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] parts = text.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries
        );

        if (parts.Length < 5)
        {
            return true;
        }

        if (!int.TryParse(parts[1], out int sampleRate) ||
            !int.TryParse(parts[2], out int blockSize) ||
            !int.TryParse(parts[3], out int channels))
        {
            return true;
        }

        hello = new HelloOptions(
            sampleRate,
            blockSize,
            channels,
            parts[4]
        );

        return true;
    }

    private async Task<bool> TryHandleNoteOnAsync(string text)
    {
        if (!text.StartsWith("note on ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] parts = text.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries
        );

        if (parts.Length < 3)
        {
            return true;
        }

        if (!int.TryParse(parts[2], out int note))
        {
            return true;
        }

        float velocity = 1f;

        if (parts.Length > 3)
        {
            float.TryParse(
                parts[3],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out velocity
            );
        }

        await _pluginEventRepository.AddControlEventAsync(
            _pluginId,
            PluginEvent.NoteOn(
                _pluginId,
                note,
                velocity
            )
        );

        return true;
    }

    private async Task<bool> TryHandleNoteOffAsync(string text)
    {
        if (!text.StartsWith("note off ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] parts = text.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries
        );

        if (parts.Length < 3)
        {
            return true;
        }

        if (!int.TryParse(parts[2], out int note))
        {
            return true;
        }

        await _pluginEventRepository.AddControlEventAsync(
            _pluginId,
            PluginEvent.NoteOff(
                _pluginId,
                note
            )
        );

        return true;
    }

    private async Task<bool> TryHandleParamAsync(string text)
    {
        if (!text.StartsWith("param ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] parts = text.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries
        );

        if (parts.Length < 3)
        {
            return true;
        }

        if (!uint.TryParse(parts[1], out uint parameterId))
        {
            return true;
        }

        if (!float.TryParse(
            parts[2],
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out float value
        ))
        {
            return true;
        }

        await _pluginEventRepository.AddControlEventAsync(
            _pluginId,
            PluginEvent.Param(
                _pluginId,
                parameterId,
                value
            )
        );

        return true;
    }

    private async Task<bool> TryHandlePanicAsync(string text)
    {
        if (!text.Equals("panic", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        await _pluginEventRepository.AddControlEventAsync(
            _pluginId,
            PluginEvent.Panic(_pluginId)
        );

        return true;
    }

    private static bool TryHandleCreditMessage(
        ReadOnlySpan<byte> span,
        Action<int> addCredits,
        SemaphoreSlim creditSignal
    )
    {
        if (span.Length != 8 ||
            span[0] != 'C' ||
            span[1] != 'R' ||
            span[2] != 'D' ||
            span[3] != '0')
        {
            return false;
        }

        uint blocks = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);

        if (blocks == 0)
        {
            return true;
        }

        int creditsToAdd = blocks > int.MaxValue
            ? int.MaxValue
            : (int)blocks;

        addCredits(creditsToAdd);

        for (int i = 0; i < creditsToAdd; i++)
        {
            creditSignal.Release();
        }

        return true;
    }

    private static bool TryParseEvt1Message(
        ReadOnlySpan<byte> span,
        out ulong seq,
        out int blockFrames,
        out List<Evt> events
    )
    {
        seq = 0;
        blockFrames = 0;
        events = [];

        if (span.Length < 4 ||
            span[0] != 'E' ||
            span[1] != 'V' ||
            span[2] != 'T' ||
            span[3] != '1')
        {
            return false;
        }

        return TryParseEvt1(
            span,
            out seq,
            out blockFrames,
            out events
        );
    }

    private static bool TryParseEvt1(
        ReadOnlySpan<byte> span,
        out ulong seq,
        out int blockFrames,
        out List<Evt> evts
    )
    {
        evts = [];
        seq = 0;
        blockFrames = 0;

        if (span.Length < 20)
        {
            return false;
        }

        if (!(span[0] == 'E' && span[1] == 'V' && span[2] == 'T' && span[3] == '1'))
        {
            return false;
        }

        seq = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(4, 8));
        blockFrames = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(12, 4));
        uint cnt = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(16, 4));

        if (blockFrames <= 0)
        {
            return false;
        }

        int pos = 20;
        const int entrySize = 12;

        if (cnt > int.MaxValue / entrySize)
        {
            return false;
        }

        if (span.Length < pos + entrySize * (int)cnt)
        {
            return false;
        }

        for (uint i = 0; i < cnt; i++, pos += entrySize)
        {
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(pos + 0, 2));
            ushort pitch = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(pos + 2, 2));

            float vel = BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32LittleEndian(span.Slice(pos + 4, 4))
            );

            ushort offs = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(pos + 8, 2));

            evts.Add(new Evt(type, pitch, vel, offs));
        }

        evts.Sort((a, b) =>
            a.Offs != b.Offs
                ? a.Offs.CompareTo(b.Offs)
                : a.Type.CompareTo(b.Type)
        );

        return true;
    }

    private async Task RunPluginCacheLeaseLoopAsync(
        IRuntimeChannel ch,
        CancellationTokenSource sessionCts
    )
    {
        CancellationToken ct = sessionCts.Token;

        TimeSpan delay = PluginCacheTtl - PluginCacheRenewBefore;

        if (delay <= TimeSpan.Zero)
        {
            delay = TimeSpan.FromSeconds(30);
        }

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(
                    delay,
                    ct
                );

                string token = Guid.NewGuid().ToString("N");

                var tcs = new TaskCompletionSource<string>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );

                lock (_pluginCacheRenewLock)
                {
                    _expectedPluginCacheRenewToken = token;
                    _pluginCacheRenewTcs = tcs;
                }

                await ch.SendAsync(
                    Encoding.UTF8.GetBytes($"plugin cache renew {token}"),
                    "text/plain",
                    endOfMessage: true,
                    ct
                );

                Task completed = await Task.WhenAny(
                    tcs.Task,
                    Task.Delay(
                        PluginCacheRenewResponseTimeout,
                        ct
                    )
                );

                if (completed != tcs.Task)
                {
                    _log.LogWarning(
                        "Plugin cache renew confirmation timeout. Closing websocket session. PluginId={PluginId}",
                        _pluginId
                    );

                    await sessionCts.CancelAsync();
                    return;
                }

                await tcs.Task;

                RepositoryActionStatus status = await _pluginRepository.RefreshTtlAsync(
                    _pluginId
                );

                if (status != RepositoryActionStatus.Success)
                {
                    _log.LogWarning(
                        "Plugin cache TTL refresh failed. Closing websocket session. PluginId={PluginId}, Status={Status}",
                        _pluginId,
                        status
                    );

                    await sessionCts.CancelAsync();
                    return;
                }

                _log.LogDebug(
                    "Plugin cache TTL refreshed. PluginId={PluginId}",
                    _pluginId
                );

                lock (_pluginCacheRenewLock)
                {
                    if (_expectedPluginCacheRenewToken == token)
                    {
                        _expectedPluginCacheRenewToken = null;
                        _pluginCacheRenewTcs = null;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Нормальное завершение.
        }
        finally
        {
            lock (_pluginCacheRenewLock)
            {
                _expectedPluginCacheRenewToken = null;
                _pluginCacheRenewTcs = null;
            }
        }
    }

    private async Task RunProcessingLoopAsync(
        IRuntimeChannel ch,
        int sampleRate,
        int blockSize,
        int channels,
        bool offline,
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

        int latencyFrames = offline
            ? CalculateRenderDelayFrames(sampleRate, blockSize)
            : CalculateRealtimeDelayFrames(sampleRate);

        string beginMessage = offline
            ? $"render begin {_currentEpoch} {latencyFrames} {RenderInitialPrefillBlocks}"
            : $"realtime begin {_currentEpoch} {latencyFrames}";

        await ch.SendAsync(
            Encoding.UTF8.GetBytes(beginMessage),
            "text/plain",
            true,
            ct
        );

        _log.LogInformation(
            "Audio session begin sent. PluginId={PluginId}, Epoch={Epoch}, Mode={Mode}, SampleRate={SampleRate}, BlockSize={BlockSize}, Channels={Channels}, LatencyFrames={LatencyFrames}",
            _pluginId.GetValue(),
            _currentEpoch,
            offline ? "offline" : "realtime",
            sampleRate,
            blockSize,
            channels,
            latencyFrames
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
            _log.LogInformation(
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

            PluginBlockProcessResult result = await _pluginBlockProcessor.ProcessBlockAsync(
                _pluginId,
                seq,
                framesForBlock,
                offline,
                ct
            );

            Volatile.Write(ref _lastProcessedSeq, (long)seq);

            if (result.ShouldSend)
            {
                byte[] frame = Aud1.Pack(
                    epoch,
                    seq,
                    ts,
                    sampleRate,
                    channels,
                    result.Audio
                );

                await ch.SendAsync(
                    frame,
                    "application/octet-stream",
                    endOfMessage: true,
                    ct
                );
            }

            if (offline)
            {
                decrementCredits();
            }

            seq++;
            ts += (ulong)framesForBlock;

            if (!offline)
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

    private bool TryHandlePluginCacheRenewConfirmation(string text)
    {
        const string prefix = "plugin cache renew ok ";

        if (!text.StartsWith(
            prefix,
            StringComparison.OrdinalIgnoreCase
        ))
        {
            return false;
        }

        string token = text[prefix.Length..].Trim();

        if (string.IsNullOrWhiteSpace(token))
        {
            return true;
        }

        TaskCompletionSource<string>? tcs = null;

        lock (_pluginCacheRenewLock)
        {
            if (_expectedPluginCacheRenewToken == token)
            {
                tcs = _pluginCacheRenewTcs;
                _expectedPluginCacheRenewToken = null;
                _pluginCacheRenewTcs = null;
            }
        }

        tcs?.TrySetResult(token);

        return true;
    }

    private static int CalculateRealtimeDelayFrames(int sampleRate)
    {
        sampleRate = NormalizePositive(sampleRate, DefaultSampleRate);

        int frames = (int)Math.Round(sampleRate * RealtimeDefaultLatencySeconds);

        // Чтобы не получить слишком маленький prebuffer на странных sample rate.
        return Math.Max(256, frames);
    }

    private static int CalculateRenderDelayFrames(int sampleRate, int blockSize)
    {
        sampleRate = NormalizePositive(sampleRate, DefaultSampleRate);
        blockSize = NormalizePositive(blockSize, DefaultBlockSize);

        int frames = (int)Math.Round(sampleRate * OfflineRenderLatencySeconds);

        // Offline должен заранее готовить больший запас, чем realtime.
        // Но RenderInitialPrefillBlocks остаётся 0: сервер не должен забегать вперёд по блокам.
        return Math.Max(blockSize, frames);
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

    private sealed record HelloOptions(
        int SampleRate,
        int BlockSize,
        int Channels,
        string Mode
    );

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
