using System.Collections.Concurrent;
using System.Diagnostics;
using ComposeNowPlugins.Wrappers;

namespace ComposeNowPlugins.Transports;

public sealed class AudioRuntimeSession : IRuntimeSession
{
    private readonly ILogger<AudioRuntimeSession> _log;
    private readonly VstEngine _vst;
    private readonly IConfiguration _cfg;

    private readonly int _sampleRate;
    private readonly int _channels = 2;
    private readonly int _blockSize;

    // задержка
    private readonly int _delayMs;
    private readonly int _delaySamples;
    private readonly Queue<float> _delayFifo = new();

    private readonly ConcurrentDictionary<ulong, List<(ushort type, ushort pitch, float vel, ushort offs)>> _evtBySeq = new();

    public AudioRuntimeSession(ILogger<AudioRuntimeSession> log, VstEngine vst, IConfiguration cfg)
    {
        _log = log;
        _vst = vst;
        _cfg = cfg;

        _sampleRate = vst.SampleRate;
        _blockSize = vst.BlockSize;

        _delayMs = /*int.Parse(cfg["AUDIO_DEV_DELAY_MS"] ?? "0")*/0;
        _delaySamples = (int)(_sampleRate * (_delayMs / 1000.0));
    }

    public async Task RunAsync(IRuntimeChannel ch, CancellationToken ct)
    {
        // 0) --- HANDSHAKE: ждём hello от клиента (ТЕКСТ) ---
        int sampleRate = _sampleRate, channels = _channels, blockSize = _blockSize;
        var mode = "realtime";

        // 1) фьючерс, который выполнится при получении hello
        var helloTcs = new TaskCompletionSource<(int sampleRate, int blockSize, int channels, string mode)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // кредитный счётчик и сигнал
        int credits = 0;
        var creditSignal = new SemaphoreSlim(0, int.MaxValue);

        // 1) Параллельное чтение входа (ноты/параметры)
        var reader = Task.Run(async () =>
        {
            await foreach (var msg in ch.ReadAllAsync(ct))
            {
                var span = msg.Payload.Span;

                var s = System.Text.Encoding.UTF8.GetString(span).Trim();
                if (s.StartsWith("hello ", StringComparison.OrdinalIgnoreCase))
                {
                    var p = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length >= 5)
                    {
                        int _sr = int.Parse(p[1]);
                        int _bs = int.Parse(p[2]);
                        int _ch = int.Parse(p[3]);
                        string _mode = p[4];
                        // выставляем результат hello — только один раз
                        helloTcs.TrySetResult((_sr, _bs, _ch, _mode));
                    }
                    continue;
                }

                if (s.StartsWith("note on ", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var note = int.Parse(parts[2]);
                    var vel = parts.Length > 3 ? float.Parse(parts[3]) : 1f;
                    _vst.NoteOn(note, vel);
                    continue;
                }
                if (s.StartsWith("note off ", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var note = int.Parse(parts[2]);
                    _vst.NoteOff(note);
                    continue;
                }
                if (s.StartsWith("param ", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var id = uint.Parse(parts[1]);
                    var norm = float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                    _vst.SetParam(id, norm);
                    continue;
                }

                // бинарные управляющие кадры
                if (span.Length == 8 &&
                    span[0] == 'C' && span[1] == 'R' && span[2] == 'D' && span[3] == '0')
                {
                    uint blocks = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
                    if (blocks > 0)
                    {
                        Interlocked.Add(ref credits, (int)blocks);
                        creditSignal.Release(); // разбудим продюсера
                    }
                    continue;
                }

                if (span.Length >= 16 && span[0] == 'E' && span[1] == 'V' && span[2] == 'T' && span[3] == '0')
                {
                    ulong seq = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(4, 8));
                    uint count = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(12, 4));
                    var list = new List<(ushort, ushort, float, ushort)>((int)count);

                    var payload = span.Slice(16);
                    int off = 0;
                    for (int i = 0; i < count; i++)
                    {
                        ushort type = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(off)); off += 2;
                        ushort pitch = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(off)); off += 2;
                        float vel = BitConverter.ToSingle(payload.Slice(off, 4)); off += 4;
                        ushort so = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(off)); off += 2;
                        off += 2; // pad
                        list.Add((type, pitch, vel, so));
                    }
                    _evtBySeq[seq] = list;
                    continue;
                }
            }
        }, ct);

        // 2) Стрим аудио с тактированием по blockSize/sampleRate
        var period = TimeSpan.FromSeconds((double)blockSize / sampleRate);
        var next = Stopwatch.GetTimestamp();
        var freq = (double)Stopwatch.Frequency;

        ulong seq = 0, ts = 0;

        // 3) ждём hello с таймаутом (чтобы не зависнуть, если клиент его не шлёт)
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2)); // таймаут хэндшейка (на вкус)
            var res = await helloTcs.Task.WaitAsync(cts.Token);
            sampleRate = res.sampleRate;
            blockSize = res.blockSize;
            channels = res.channels;
            mode = res.mode;
        }
        catch (OperationCanceledException)
        {
            // hello не пришёл вовремя — остаёмся на дефолтах (_sampleRate/_blockSize/_channels, realtime)
        }

        var offline = string.Equals(mode, "offline", StringComparison.OrdinalIgnoreCase);

        var ok = _vst.Reconfigure(sampleRate, blockSize, channels, offline);
        if (!ok)
        {
            _log.LogWarning("VST reconfigure failed: sr={sr}, bs={bs}, ch={ch}, offline={offline}",
                sampleRate, blockSize, channels, offline);
            // опционально: откатиться к дефолтам или завершить сессию
            // return; // если хочешь оборвать поток
        }

        // обновляем тайминг под новые параметры
        period = TimeSpan.FromSeconds((double)blockSize / sampleRate);
        next = Stopwatch.GetTimestamp();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // ждём кредит в оффлайне и в реальном времени — одинаково безопасно
                if (offline)
                {
                    while (Volatile.Read(ref credits) <= 0)
                        await creditSignal.WaitAsync(ct);
                }

                byte[]? frame;

                var events = _evtBySeq.TryGetValue(seq, out var lst)
                    ? lst.OrderBy(e => e.offs)
                        .ThenBy(e => e.type == 2 ? 0 : 1) // Off (2) перед On (1)
                        .ToList()
                    : null;
                if (events != null && events.Count > 0)
                {
                    int cursor = 0;
                    var outMix = new float[blockSize * channels];
                    // рендерим по кускам: [cursor .. ev.offs), применяем событие, дальше...
                    foreach (var (type, pitch, vel, offs) in events)
                    {
                        int len = Math.Clamp(offs - cursor, 0, blockSize - cursor);
                        if (len > 0)
                        {
                            var part = _vst.Process(len);
                            MixInto(outMix, part, cursor, channels);
                            cursor += len;
                        }
                        if (type == 1) _vst.NoteOn(pitch, vel);
                        else if (type == 2) _vst.NoteOff(pitch);
                    }
                    // хвост до конца блока
                    if (cursor < blockSize)
                    {
                        var tail = _vst.Process(blockSize - cursor);
                        MixInto(outMix, tail, cursor, channels);
                    }
                    // теперь упаковываем outMix
                    frame = Aud0.Pack(seq, ts, sampleRate, channels, outMix);

                    _evtBySeq.TryRemove(seq, out _);
                }
                else
                {
                    // нет событий — обычный сплошной Process()
                    var audio = _vst.Process();
                    frame = Aud0.Pack(seq, ts, sampleRate, channels, audio);
                }

                //var frame = Aud0.Pack(seq, ts, sampleRate, channels, audio);
                await ch.SendAsync(frame, "application/octet-stream", true, ct);

                if (offline)
                {
                    Interlocked.Decrement(ref credits);
                }

                seq++;
                ts += (ulong)blockSize;

                // в оффлайне — без задержек!
                if (!offline)
                {
                    next += (long)(period.TotalSeconds * freq);
                    var delay = TimeSpan.FromSeconds((next - Stopwatch.GetTimestamp()) / freq);
                    if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
                }
            }
        }
        finally
        {
            try { await reader; } catch { /* ignore */ }
        }
    }
    
    private static void MixInto(float[] dst, ReadOnlyMemory<float> src, int dstFrameOffset, int channels)
    {
        var s = src.Span;
        int frames = s.Length / channels;
        int dstIndex = dstFrameOffset * channels;
        // простое суммирование (если планируешь громкость держать, можно заменить на копирование)
        for (int i = 0; i < frames * channels; i++)
            dst[dstIndex + i] = s[i];
    }
}
