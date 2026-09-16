using ComposeNowPlugins.Domain.Configurations;
using ComposeNowPlugins.Application.Services.Processing;

namespace ComposeNowPlugins.Worker.Wrappers;

public sealed class VstEngine : IPluginEngine, IAsyncDisposable
{
    private readonly ILogger? _log;
    private IntPtr _host;
    private int _sampleRate;
    private int _blockSize;
    private int _channels = 2;
    private float[] _tmp;
    private float[] _inputTmp;
    private readonly Dictionary<uint, float> _appliedParameters = new();
    private readonly int _maxStateBytes;
    private ulong _knownStateHash;
    private int _knownStateLength = -1;

    public VstEngine(
        string pluginPath,
        ILogger<VstEngine> log,
        int sampleRate = 44100,
        int blockSize = 512,
        int channels = 2,
        int maxStateBytes = 64 * 1024 * 1024
    )
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

        ValidateConfiguration(sampleRate, blockSize, channels);
        _sampleRate = sampleRate;
        _blockSize = blockSize;
        _channels = channels;
        _maxStateBytes = maxStateBytes > 0
            ? maxStateBytes
            : throw new ArgumentOutOfRangeException(nameof(maxStateBytes));

        int initialBufferSize = checked(_blockSize * _channels);
        _tmp = new float[initialBufferSize];
        _inputTmp = new float[initialBufferSize];

        IntPtr host = VstNative.VstCreate(pluginPath, _sampleRate, _blockSize, _channels);
        if (host == IntPtr.Zero)
        {
            throw new InvalidOperationException($"vst_create failed for plugin: {pluginPath}");
        }

        try
        {
            var devDelayMs = 0;
            var latencySamples = (uint)(_sampleRate * (devDelayMs / 1000.0));
            VstNative.VstSetLatency(host, latencySamples);
            _host = host;
        }
        catch
        {
            try
            {
                VstNative.VstDestroy(host);
            }
            catch
            {
                // Preserve the initialization failure.
            }

            throw;
        }

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

    public void NoteOn(int note, float vel = 1f)
    {
        VstNative.VstNoteOn(GetHost(), note, vel);
    }

    public void NoteOff(int note)
    {
        VstNative.VstNoteOff(GetHost(), note);
    }

    public void SetParam(uint id, float norm)
    {
        VstNative.VstSetParam(GetHost(), id, norm);
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

    public void SetParameterIfChanged(uint id, float value)
    {
        SetParamIfChanged(id, value);
    }

    // Generate the next audio chunk (interleaved float32).
    public ReadOnlyMemory<float> Process()
    {
        return Process(_blockSize);
    }

    public ReadOnlyMemory<float> Process(int frames)
    {
        if (frames <= 0) return ReadOnlyMemory<float>.Empty;

        // Ensure the buffer has the required size.
        int need = checked(frames * _channels);
        if (_tmp.Length < need)
            _tmp = new float[need];

        int n;
        unsafe
        {
            fixed (float* outPtr = _tmp)
            {
                n = VstNative.VstProcess(GetHost(), outPtr, frames);
            }
        }

        EnsureProcessedFrameCount(n, frames);
        return new ReadOnlyMemory<float>(_tmp, 0, n * _channels);
    }

    // Effect processing: interleaved float32 input -> interleaved float32 output
    public ReadOnlyMemory<float> Process(ReadOnlySpan<float> input, int frames)
    {
        if (frames <= 0) return ReadOnlyMemory<float>.Empty;

        int need = checked(frames * _channels);
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
                n = VstNative.VstProcessReplacing(GetHost(), inPtr, outPtr, frames);
            }
        }

        EnsureProcessedFrameCount(n, frames);
        return new ReadOnlyMemory<float>(_tmp, 0, n * _channels);
    }

    public ValueTask DisposeAsync()
    {
        DestroyHost();
        GC.SuppressFinalize(this);

        return ValueTask.CompletedTask;
    }

    ~VstEngine()
    {
        try
        {
            DestroyHost();
        }
        catch
        {
            // Finalizers must not propagate native cleanup failures.
        }
    }

    public bool Reconfigure(int sampleRate, int blockSize, int channels, bool offline)
    {
        ValidateConfiguration(sampleRate, blockSize, channels);

        var ok = VstNative.VstReconfigure(GetHost(), sampleRate, blockSize, channels, offline ? 1 : 0);
        if (!ok) return false;

        _sampleRate = sampleRate;
        _blockSize = blockSize;
        _channels = channels;
        int bufferSize = checked(_blockSize * _channels);
        if (_tmp.Length != bufferSize)
            _tmp = new float[bufferSize];
        if (_inputTmp.Length != bufferSize)
            _inputTmp = new float[bufferSize];
        _appliedParameters.Clear();

        _log?.LogDebug("VstEngine reconfigured: {SR} Hz, block {Block}, ch {Ch}, offline={Offline}",
            _sampleRate, _blockSize, _channels, offline);
        return true;
    }

    /// <summary>
    /// Capture the current binary VST snapshot (component and controller state).
    /// </summary>
    public unsafe byte[] GetState()
    {
        ObjectDisposedException.ThrowIf(_host == IntPtr.Zero, nameof(VstEngine));

        uint size = 0;

        // 1) Query the size.
        if (!VstNative.VstGetState(_host, IntPtr.Zero, ref size))
        {
            throw new InvalidOperationException("VstGetState (size query) failed.");
        }

        if (size == 0)
        {
            MarkKnownState(ReadOnlySpan<byte>.Empty);
            return [];
        }

        if (size > _maxStateBytes)
        {
            throw new InvalidOperationException(
                $"VST state exceeds the configured limit. Size={size}, Limit={_maxStateBytes}."
            );
        }

        var buf = new byte[size];

        // 2) Retrieve the state.
        fixed (byte* p = buf)
        {
            var ptr = (IntPtr)p;
            if (!VstNative.VstGetState(_host, ptr, ref size))
                throw new InvalidOperationException("VstGetState (data) failed.");
        }

        // The actual size may be smaller than the initial estimate; trim the buffer.
        if (size > buf.Length)
        {
            throw new InvalidOperationException("VST returned a state larger than the provided buffer.");
        }

        if (size < buf.Length)
        {
            Array.Resize(ref buf, checked((int)size));
        }

        MarkKnownState(buf);
        return buf;
    }

    /// <summary>
    /// Restore a binary VST state snapshot.
    /// </summary>
    public void SetState(byte[]? state)
    {
        ObjectDisposedException.ThrowIf(_host == IntPtr.Zero, nameof(VstEngine));

        state ??= [];

        EnsureStateSize(state);

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
        EnsureStateSize(state);
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
            int mode = VstNative.VstGetProcessMode(GetHost());

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

    private IntPtr GetHost()
    {
        IntPtr host = _host;
        ObjectDisposedException.ThrowIf(host == IntPtr.Zero, this);
        return host;
    }

    private void DestroyHost()
    {
        IntPtr host = Interlocked.Exchange(ref _host, IntPtr.Zero);
        if (host != IntPtr.Zero)
        {
            VstNative.VstDestroy(host);
        }
    }

    private static void EnsureProcessedFrameCount(int processedFrames, int requestedFrames)
    {
        if (processedFrames < 0 || processedFrames > requestedFrames)
        {
            throw new InvalidOperationException(
                $"VST returned an invalid frame count. Requested={requestedFrames}, Processed={processedFrames}."
            );
        }
    }

    private void EnsureStateSize(byte[] state)
    {
        if (state.Length > _maxStateBytes)
        {
            throw new InvalidOperationException(
                $"VST state exceeds the configured limit. Size={state.Length}, Limit={_maxStateBytes}."
            );
        }
    }

    private static void ValidateConfiguration(int sampleRate, int blockSize, int channels)
    {
        if (sampleRate is <= 0 or > AudioProcessingLimits.MaxSampleRate)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        if (blockSize is <= 0 or > AudioProcessingLimits.MaxFramesPerBlock)
        {
            throw new ArgumentOutOfRangeException(nameof(blockSize));
        }

        if (channels is <= 0 or > AudioProcessingLimits.MaxChannels)
        {
            throw new ArgumentOutOfRangeException(nameof(channels));
        }
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
