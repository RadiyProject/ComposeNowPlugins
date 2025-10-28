namespace ComposeNowPlugins.Wrappers;

public sealed class VstEngine : IAsyncDisposable
{
    private readonly ILogger? _log;
    private readonly IntPtr _host;
    private int _sampleRate;
    private int _blockSize;
    private int _channels = 2;
    private float[] _tmp;

    public VstEngine(IConfiguration cfg, ILogger<VstEngine> log, int? sampleRate = null, int? blockSize = null, int? channels = null)
    {
        _log = log;

        var pluginPath = cfg["VST3_PATH"]!; // напр.: /app/plugins/SineSynth.vst3
        _sampleRate = sampleRate ?? int.Parse(cfg["AUDIO_SAMPLE_RATE"] ?? "48000");
        _blockSize = blockSize ?? int.Parse(cfg["AUDIO_BLOCK_SIZE"] ?? "512");

        _host = VstNative.VstCreate(pluginPath, _sampleRate, _blockSize, _channels);
        if (_host == IntPtr.Zero) throw new InvalidOperationException("vst_create failed");

        var devDelayMs = /*int.Parse(cfg["AUDIO_DEV_DELAY_MS"] ?? "0")*/0;
        var latencySamples = (uint)(_sampleRate * (devDelayMs / 1000.0));
        VstNative.VstSetLatency(_host, latencySamples);

        _tmp = new float[_blockSize * _channels];

        _log?.LogInformation("VstEngine ready: {Path} @ {SR} Hz, block {Block}, ch {Ch}, latency {delay} ms",
            pluginPath, _sampleRate, _blockSize, _channels, devDelayMs);
    }

    // public VstEngine(
    //     ILogger<VstEngine>? log,
    //     string pluginPath,
    //     int sampleRate,
    //     int blockSize,
    //     int channels)
    // {
    //     _log = log;
    //     if (string.IsNullOrWhiteSpace(pluginPath))
    //         throw new ArgumentException("Plugin path is empty", nameof(pluginPath));

    //     _sampleRate = sampleRate > 0 ? sampleRate : 44100;
    //     _blockSize  = blockSize  > 0 ? blockSize  : 512;
    //     _channels   = channels   > 0 ? channels   : 2;

    //     _host = VstNative.VstCreate(pluginPath, _sampleRate, _blockSize, _channels);
    //     if (_host == IntPtr.Zero)
    //         throw new InvalidOperationException($"VstCreate failed for '{pluginPath}'");

    //     _tmp = new float[_blockSize * _channels];

    //     _log?.LogInformation("VstEngine ready: {Path} @ {SR} Hz, block {Block}, ch {Ch}",
    //         pluginPath, _sampleRate, _blockSize, _channels);
    // }

    public int SampleRate => _sampleRate;
    public int BlockSize => _blockSize;
    public int Channels   => _channels;

    public void NoteOn(int note, float vel = 1f) => VstNative.VstNoteOn(_host, note, vel);
    public void NoteOff(int note) => VstNative.VstNoteOff(_host, note);

    public void SetParam(uint id, float norm) => VstNative.VstSetParam(_host, id, norm);

    // генерируем следующий аудио-чанк (interleaved float32)
    public ReadOnlyMemory<float> Process()
    {
        var n = VstNative.VstProcess(_host, _tmp, _blockSize);
        return new ReadOnlyMemory<float>(_tmp, 0, n * _channels);
    }

    public ValueTask DisposeAsync()
    {
        if (_host != IntPtr.Zero) VstNative.VstDestroy(_host);
        return ValueTask.CompletedTask;
    }

    public bool Reconfigure(int? sampleRate, int? blockSize, int? channels, bool offline)
    {
        var sr = Math.Max(1, sampleRate ?? _sampleRate);
        var bs = Math.Max(1, blockSize  ?? _blockSize);
        var ch = Math.Max(1, channels ?? _channels);

        var ok = VstNative.VstReconfigure(_host, sr, bs, ch, offline ? 1 : 0);
        if (!ok) return false;

        _sampleRate = sr;
        _blockSize  = bs;
        _channels   = ch;
        if (_tmp.Length != _blockSize * _channels)
            _tmp = new float[_blockSize * _channels];

        _log?.LogInformation("VstEngine reconfigured: {SR} Hz, block {Block}, ch {Ch}, offline={Offline}",
            _sampleRate, _blockSize, _channels, offline);
        return true;
    }
}
