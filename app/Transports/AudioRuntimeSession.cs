using System.Buffers.Binary;
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

    record Evt(int Type, int Pitch, float Vel, int Offs); // Type: 1=on, 2=off
    record EvtBlock(int BlockFrames, List<Evt> Events);
    readonly ConcurrentDictionary<ulong, EvtBlock> _eventsBySeq = new();

    static bool TryParseEvt1(ReadOnlySpan<byte> span, out ulong seq, out int blockFrames, out List<Evt> evts)
    {
        evts = [];
        seq = 0;
        blockFrames = 0;

        // min: 4(magic)+8(seq)+4(blockFrames)+4(cnt) = 20
        if (span.Length < 20) return false;
        if (!(span[0]=='E' && span[1]=='V' && span[2]=='T' && span[3]=='1')) return false;

        seq         = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(4, 8));
        blockFrames = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(12, 4));
        uint cnt    = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(16, 4));

        if (blockFrames <= 0) return false;

        int pos = 20;
        const int entry = 12;
        if (span.Length < pos + entry * cnt) return false;

        for (uint i = 0; i < cnt; i++, pos += entry)
        {
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(pos+0,2));
            ushort pitch= BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(pos+2,2));
            float  vel  = BitConverter.Int32BitsToSingle(
                            BinaryPrimitives.ReadInt32LittleEndian(span.Slice(pos+4,4)));
            ushort offs = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(pos+8,2));

            evts.Add(new Evt(type, pitch, vel, offs));
        }

        // сортировка по offs, затем OFF перед ON
        evts.Sort((a,b) => a.Offs != b.Offs ? a.Offs.CompareTo(b.Offs) : a.Type.CompareTo(b.Type));
        return true;
    }


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

                if (span.Length >= 4 && span[0]=='E' && span[1]=='V' && span[2]=='T' && span[3]=='1')
                {
                    if (TryParseEvt1(span, out var sseq, out var frames, out var list))
                    {
                        _eventsBySeq.AddOrUpdate(
                            sseq,
                            _ => new EvtBlock(frames, list),
                            (_, old) =>
                            {
                                var merged = old.Events;
                                merged.AddRange(list);
                                merged.Sort((a,b) => a.Offs != b.Offs ? a.Offs.CompareTo(b.Offs) : a.Type.CompareTo(b.Type));
                                // если вдруг придёт другой frames для того же seq — можно взять max/последний
                                return old with { BlockFrames = Math.Max(old.BlockFrames, frames) };
                            });
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

                float[]? outBuf = null;

                // достаём события для текущего seq (если есть)
                EvtBlock? blk = null;

                if (offline)
                {
                    var t0 = Stopwatch.GetTimestamp();
                    var frequency = (double)Stopwatch.Frequency;
                    while (!_eventsBySeq.TryRemove(seq, out blk))
                    {
                        var elapsed = (Stopwatch.GetTimestamp() - t0) / frequency;
                        if (elapsed > 0.050) break;
                        await Task.Delay(0, ct);
                    }
                }
                else
                {
                    _eventsBySeq.TryRemove(seq, out blk);
                }
                List<Evt>? evtsForBlock = blk?.Events;
                int framesForBlock = blk?.BlockFrames ?? _vst.BlockSize;
                int block = framesForBlock;

                if (evtsForBlock is null || evtsForBlock.Count == 0)
                {
                    // как раньше — один заход
                    var audio = _vst.Process(block);
                    // упаковка...
                    var frame = Aud0.Pack(seq, ts, sampleRate, channels, audio);
                    await ch.SendAsync(frame, "application/octet-stream", true, ct);
                }
                else
                {
                    // Гарантируем размер под весь блок и очищаем на всякий случай
                    if (outBuf is null || outBuf.Length != block * channels)
                        outBuf = new float[block * channels];
                    else
                        Array.Clear(outBuf, 0, outBuf.Length);

                    int cursor = 0;
                    foreach (var ev in evtsForBlock)
                    {
                        int offs = Math.Clamp(ev.Offs, 0, block-1);
                        int len = Math.Max(0, offs - cursor);
                        if (len > 0)
                        {
                            var part = _vst.Process(len);
                            // копируем part в outBuf на позицию cursor
                            var partArr = part.ToArray(); // если VstProcess не гарантирует фиксированный буфер
                            Buffer.BlockCopy(partArr, 0, outBuf, cursor * channels * sizeof(float), partArr.Length * sizeof(float));
                            cursor += len;
                        }

                        // применяем событие на точном сэмпле
                        if (ev.Type == 2) _vst.NoteOff(ev.Pitch);     // OFF раньше ON при одинаковом offs
                        else              _vst.NoteOn(ev.Pitch, ev.Vel);
                    }
                    // дорисовываем хвост блока
                    if (cursor < block)
                    {
                        var tail = _vst.Process(block - cursor);
                        var tailArr = tail.ToArray();
                        Buffer.BlockCopy(tailArr, 0, outBuf, cursor * channels * sizeof(float), tailArr.Length * sizeof(float));
                    }

                    // пакуем готовый interleaved буфер
                    var frame = Aud0.Pack(seq, ts, sampleRate, channels, outBuf);
                    await ch.SendAsync(frame, "application/octet-stream", true, ct);
                }

                if (offline)
                {
                    Interlocked.Decrement(ref credits);
                }

                seq++;
                ts += (ulong)framesForBlock;

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
