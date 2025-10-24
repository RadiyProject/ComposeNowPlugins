using System.Diagnostics;
using System.Text.Json;
using ComposeNowPlugins.Wrappers;

namespace ComposeNowPlugins.Transports;

public sealed class AudioRuntimeSession : IRuntimeSession
{
    private readonly ILogger<AudioRuntimeSession> _log;
    private readonly VstEngine _vst;

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

        _sampleRate = vst.SampleRate;
        _blockSize = vst.BlockSize;

        _delayMs = int.Parse(cfg["AUDIO_DEV_DELAY_MS"] ?? "0");
        _delaySamples = (int)(_sampleRate * (_delayMs / 1000.0));
    }

    public async Task RunAsync(IRuntimeChannel ch, CancellationToken ct)
    {
        // hello для клиента
        var hello = JsonSerializer.SerializeToUtf8Bytes(new {
            type = "hello",
            sample_rate = _sampleRate,
            channels = _channels,
            block = _blockSize,
            encoding = "AUD0/float32le"
        });
        await ch.SendAsync(hello, "application/json", true, ct);

        // 1) Параллельное чтение входа (ноты/параметры)
        var reader = Task.Run(async () =>
        {
            await foreach (var msg in ch.ReadAllAsync(ct))
            {
                if (msg.ContentType != "text/plain") continue;
                var s = System.Text.Encoding.UTF8.GetString(msg.Payload.Span).Trim();

                if (s.StartsWith("note on ", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var note = int.Parse(parts[2]);
                    var vel  = parts.Length > 3 ? float.Parse(parts[3]) : 1f;
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
                    var id   = uint.Parse(parts[1]);
                    var norm = float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                    _vst.SetParam(id, norm);
                    continue;
                }
            }
        }, ct);

        // 2) Стрим аудио с тактированием по blockSize/sampleRate
        var period = TimeSpan.FromSeconds((double)_blockSize / _sampleRate);
        var next = Stopwatch.GetTimestamp();
        var freq = (double)Stopwatch.Frequency;

        ulong seq = 0, ts = 0;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var audio = _vst.Process(); // interleaved float32 (_blockSize * _channels)
                var frame = Aud0.Pack(seq, ts, _sampleRate, _channels, audio);
                await ch.SendAsync(frame, "application/octet-stream", true, ct);

                seq++;
                ts += (ulong)_blockSize;

                next += (long)(period.TotalSeconds * freq);
                var delay = TimeSpan.FromSeconds((next - Stopwatch.GetTimestamp()) / freq);
                if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
            }
        }
        finally
        {
            try { await reader; } catch { /* ignore */ }
        }
    }
}

