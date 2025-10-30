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

                if (s.Equals("panic", StringComparison.OrdinalIgnoreCase))
                {
                    // мгновенно гасим все голоса
                    for (int n = 0; n < 128; n++) _vst.NoteOff(n);
                    // если у синта есть «сустейн» — принудительно отпустить (если реализовано через параметр/CC64)
                    // _vst.SetParam(CC64ParamId, 0f);
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
                
                // нет событий — обычный сплошной Process()
                var audio = _vst.Process();
                frame = Aud0.Pack(seq, ts, sampleRate, channels, audio);

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
            try
            {
                for (int n = 0; n < 128; n++) _vst.NoteOff(n);
                // короткий «дорасчёт» хвоста (если нужно) или просто ничего не шлём
            }
            catch { }
        }
    }
}
