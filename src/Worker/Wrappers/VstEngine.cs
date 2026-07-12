using ComposeNowPlugins.Configurations;

namespace ComposeNowPlugins.Wrappers;

public sealed class VstEngine : IAsyncDisposable
{
    private readonly ILogger? _log;
    private readonly IntPtr _host;
    private int _sampleRate;
    private int _blockSize;
    private int _channels = 2;
    private float[] _tmp;
    private float[] _inputTmp;
    private readonly Dictionary<uint, float> _appliedParameters = new();
    private ulong _knownStateHash;
    private int _knownStateLength = -1;

    public VstEngine(string pluginPath, ILogger<VstEngine> log, int sampleRate = 44100,
        int blockSize = 512, int channels = 2)
    {
        _log = log;
        if (string.IsNullOrWhiteSpace(pluginPath))
        {
            throw new ArgumentException("Plugin path cannot be empty.", nameof(pluginPath));
        }
        if (!File.Exists(pluginPath) && !Directory.Exists(pluginPath))
        {
            throw new FileNotFoundException($"VST plugin was not found: {pluginPath}");
        }

        _sampleRate = Math.Max(1, sampleRate);
        _blockSize = Math.Max(1, blockSize);
        _channels = Math.Max(1, channels);

        _host = VstNative.VstCreate(pluginPath, _sampleRate, _blockSize, _channels);
        if (_host == IntPtr.Zero)
        {
            throw new InvalidOperationException($"vst_create failed for plugin: {pluginPath}");
        }

        var devDelayMs = 0;
        var latencySamples = (uint)(_sampleRate * (devDelayMs / 1000.0));
        VstNative.VstSetLatency(_host, latencySamples);

        int initialBufferSize = _blockSize * _channels;
        _tmp = new float[initialBufferSize];
        _inputTmp = new float[initialBufferSize];

        _log?.LogDebug(
            "VstEngine ready: {Path} @ {SR} Hz, block {Block}, ch {Ch}",
            pluginPath,
            _sampleRate,
            _blockSize,
            _channels
        );
    }

    public int SampleRate => _sampleRate;
    public int BlockSize => _blockSize;
    public int Channels   => _channels;

    public void NoteOn(int note, float vel = 1f) => VstNative.VstNoteOn(_host, note, vel);
    public void NoteOff(int note) => VstNative.VstNoteOff(_host, note);

    public void SetParam(uint id, float norm)
    {
        VstNative.VstSetParam(_host, id, norm);
        _appliedParameters[id] = norm;
    }

    public void SetParamIfChanged(uint id, float norm)
    {
        if (_appliedParameters.TryGetValue(id, out float current) && current.Equals(norm))
        {
            return;
        }

        SetParam(id, norm);
    }

    // генерируем следующий аудио-чанк (interleaved float32)
    public ReadOnlyMemory<float> Process()
    {
        return Process(_blockSize);
    }

    public ReadOnlyMemory<float> Process(int frames)
    {
        if (frames <= 0) return ReadOnlyMemory<float>.Empty;

        // гарантируем буфер нужного размера
        int need = frames * _channels;
        if (_tmp.Length < need)
            _tmp = new float[need];

        int n;
        unsafe
        {
            fixed (float* outPtr = _tmp)
            {
                n = VstNative.VstProcess(_host, outPtr, frames);
            }
        }

        // VstProcess возвращает фактически отрисованные фреймы (<= frames)
        return new ReadOnlyMemory<float>(_tmp, 0, n * _channels);
    }

    // обработка эффекта: interleaved float32 input -> interleaved float32 output
    public ReadOnlyMemory<float> Process(ReadOnlySpan<float> input, int frames)
    {
        if (frames <= 0) return ReadOnlyMemory<float>.Empty;

        int need = frames * _channels;
        if (_tmp.Length < need)
            _tmp = new float[need];
        if (_inputTmp.Length < need)
            _inputTmp = new float[need];

        int copied = Math.Min(input.Length, need);
        input[..copied].CopyTo(_inputTmp);
        if (copied < need)
            Array.Clear(_inputTmp, copied, need - copied);

        int n;
        unsafe
        {
            fixed (float* inPtr = _inputTmp)
            fixed (float* outPtr = _tmp)
            {
                n = VstNative.VstProcessReplacing(_host, inPtr, outPtr, frames);
            }
        }

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
        if (_inputTmp.Length != _blockSize * _channels)
            _inputTmp = new float[_blockSize * _channels];
        _appliedParameters.Clear();

        _log?.LogDebug("VstEngine reconfigured: {SR} Hz, block {Block}, ch {Ch}, offline={Offline}",
            _sampleRate, _blockSize, _channels, offline);
        return true;
    }

    /// <summary>
    /// Снять текущий бинарный снапшот состояния VST (component+controller state).
    /// </summary>
    public unsafe byte[] GetState()
    {
        ObjectDisposedException.ThrowIf(_host == IntPtr.Zero, nameof(VstEngine));

        uint size = 0;

        // 1) Первый вызов — узнать размер
        if (!VstNative.VstGetState(_host, IntPtr.Zero, ref size))
        {
            throw new InvalidOperationException("VstGetState (size query) failed.");
        }

        if (size == 0)
        {
            MarkKnownState(ReadOnlySpan<byte>.Empty);
            return [];
        }

        var buf = new byte[size];

        // 2) Второй вызов — реально забрать стейт
        fixed (byte* p = buf)
        {
            var ptr = (IntPtr)p;
            if (!VstNative.VstGetState(_host, ptr, ref size))
                throw new InvalidOperationException("VstGetState (data) failed.");
        }

        // size может быть меньше, чем первоначальная оценка, на всякий случай подрежем
        if (size != buf.Length)
        {
            Array.Resize(ref buf, checked((int)size));
        }

        MarkKnownState(buf);
        return buf;
    }

    /// <summary>
    /// Восстановить бинарный снапшот состояния VST.
    /// </summary>
    public void SetState(byte[]? state)
    {
        ObjectDisposedException.ThrowIf(_host == IntPtr.Zero, nameof(VstEngine));

        state ??= [];

        if (!VstNative.VstSetState(_host, state, (uint)state.Length))
        {
            throw new InvalidOperationException("VstSetState failed.");
        }

        MarkKnownState(state);
        _appliedParameters.Clear();
    }

    public bool SetStateIfChanged(byte[]? state)
    {
        ObjectDisposedException.ThrowIf(_host == IntPtr.Zero, nameof(VstEngine));

        state ??= [];
        ulong hash = ComputeStateHash(state);

        if (_knownStateLength == state.Length && _knownStateHash == hash)
        {
            return false;
        }

        if (!VstNative.VstSetState(_host, state, (uint)state.Length))
        {
            throw new InvalidOperationException("VstSetState failed.");
        }

        _knownStateLength = state.Length;
        _knownStateHash = hash;
        _appliedParameters.Clear();
        return true;
    }

    public PluginProcessingMode ProcessingMode
    {
        get
        {
            int mode = VstNative.VstGetProcessMode(_host);

            return mode == 1
                ? PluginProcessingMode.Offline
                : PluginProcessingMode.Realtime;
        }
    }

    private void MarkKnownState(ReadOnlySpan<byte> state)
    {
        _knownStateLength = state.Length;
        _knownStateHash = ComputeStateHash(state);
    }

    private static ulong ComputeStateHash(ReadOnlySpan<byte> state)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        ulong hash = offset;
        foreach (byte value in state)
        {
            hash ^= value;
            hash *= prime;
        }

        return hash;
    }
}
