using System.Buffers;
using ComposeNowPlugins.Domain.Configurations;
using ComposeNowPlugins.Domain.Models;
using ComposeNowPlugins.Domain.Models.Ids;
using ComposeNowPlugins.Application.Repositories.Plugins;
using ComposeNowPlugins.Application.Services.Processing;

namespace ComposeNowPlugins.Application.Services.Processing;

public sealed class ProcessPluginBlockHandler(
    IPluginEnginePool pluginEnginePool,
    IPluginRepository pluginRepository,
    IPluginEventRepository pluginEventRepository,
    IPluginProcessingGate processingGate,
    IPluginEventMerger eventMerger,
    IPluginDefaultParameterProvider defaultParameterProvider,
    IPluginEventApplier eventApplier,
    IAudioSilenceDetector silenceDetector,
    IPluginWorkerIdentity workerIdentity,
    IPluginProcessingCheckpointRepository checkpointRepository
) : IPluginBlockProcessor
{
    private readonly IPluginEnginePool _pluginEnginePool = pluginEnginePool;
    private readonly IPluginRepository _pluginRepository = pluginRepository;
    private readonly IPluginEventRepository _pluginEventRepository = pluginEventRepository;
    private readonly IPluginProcessingGate _processingGate = processingGate;
    private readonly IPluginEventMerger _eventMerger = eventMerger;
    private readonly IPluginDefaultParameterProvider _defaultParameterProvider = defaultParameterProvider;
    private readonly IPluginEventApplier _eventApplier = eventApplier;
    private readonly IAudioSilenceDetector _silenceDetector = silenceDetector;
    private readonly IPluginWorkerIdentity _workerIdentity = workerIdentity;
    private readonly IPluginProcessingCheckpointRepository _checkpointRepository = checkpointRepository;

    private const float SilenceThreshold = 0.00001f;
    private const int RequiredSilentBlocks = 16;

    public async Task<PluginBlockProcessResult> ProcessBlockAsync(
        PluginId pluginId,
        ulong epoch,
        ulong seq,
        int frames,
        bool offline,
        ReadOnlyMemory<float>? inputAudio,
        CancellationToken cancellationToken
    )
    {
        if (frames is <= 0 or > AudioProcessingLimits.MaxFramesPerBlock)
        {
            throw new ArgumentOutOfRangeException(nameof(frames));
        }

        if (inputAudio is { IsEmpty: true })
        {
            inputAudio = null;
        }

        return await _processingGate.RunAsync(
            pluginId.GetValue(),
            () => ProcessSerializedAsync(
                pluginId,
                epoch,
                seq,
                frames,
                offline,
                inputAudio,
                cancellationToken
            ),
            cancellationToken
        );
    }

    private async Task<PluginBlockProcessResult> ProcessSerializedAsync(
        PluginId pluginId,
        ulong epoch,
        ulong seq,
        int frames,
        bool offline,
        ReadOnlyMemory<float>? inputAudio,
        CancellationToken cancellationToken
    )
    {
        PluginBlockProcessResult? checkpoint = await _checkpointRepository.GetResultAsync(
            pluginId,
            epoch,
            seq
        );
        if (checkpoint is not null)
        {
            return checkpoint;
        }

        Plugin plugin = await _pluginRepository.GetAsync(pluginId)
            ?? throw new InvalidOperationException($"Plugin state was not found. PluginId={pluginId}");

        if (plugin.Channels is <= 0 or > AudioProcessingLimits.MaxChannels)
        {
            throw new InvalidOperationException(
                $"Plugin channel count is outside processing limits. PluginId={pluginId}, Channels={plugin.Channels}"
            );
        }

        if (plugin.SampleRate is <= 0 or > AudioProcessingLimits.MaxSampleRate)
        {
            throw new InvalidOperationException(
                $"Plugin sample rate is outside processing limits. PluginId={pluginId}, SampleRate={plugin.SampleRate}"
            );
        }

        if (plugin.BlockSize is <= 0 or > AudioProcessingLimits.MaxFramesPerBlock)
        {
            throw new InvalidOperationException(
                $"Plugin block size is outside processing limits. PluginId={pluginId}, BlockSize={plugin.BlockSize}"
            );
        }

        int expectedInputSamples = checked(frames * plugin.Channels);
        if (inputAudio is { IsEmpty: false } && inputAudio.Value.Length != expectedInputSamples)
        {
            throw new ArgumentException(
                $"Input audio length does not match frames and channels. Expected={expectedInputSamples}, Actual={inputAudio.Value.Length}.",
                nameof(inputAudio)
            );
        }

        IReadOnlyList<PluginEventDelivery> controlDeliveries =
            await _pluginEventRepository.ReadControlEventsAsync(pluginId, _workerIdentity.WorkerId);

        IReadOnlyList<PluginEventDelivery> blockDeliveries =
            await _pluginEventRepository.ReadBlockEventsAsync(pluginId, epoch, seq, _workerIdentity.WorkerId);

        IReadOnlyList<PluginEventDelivery> deliveries = [.. controlDeliveries, .. blockDeliveries];

        IReadOnlyList<PluginEvent> events = _eventMerger.Merge(
            pluginId,
            controlDeliveries.Select(x => x.Event).ToArray(),
            blockDeliveries.Select(x => x.Event).ToArray()
        );

        if (events.Count > AudioProcessingLimits.MaxEventsPerBlock)
        {
            throw new InvalidOperationException(
                $"Plugin event count is outside processing limits. PluginId={pluginId}, Events={events.Count}"
            );
        }

        bool hasOwnInputEvents = events.Count > 0;
        bool hasOwnActiveAudio = plugin.HasActiveAudio();
        bool hasInputAudio = inputAudio.HasValue && inputAudio.Value.Length > 0;

        if (!offline && !hasOwnInputEvents && !hasOwnActiveAudio && !hasInputAudio)
        {
            PluginBlockProcessResult emptyResult = new(
                ReadOnlyMemory<float>.Empty,
                ShouldSend: true
            );

            await _checkpointRepository.CommitAsync(
                plugin,
                _workerIdentity.WorkerId,
                epoch,
                seq,
                emptyResult,
                deliveries
            );

            return emptyResult;
        }

        PluginBlockProcessResult result = await ProcessBlockUnderLeaseAsync(
            pluginId,
            plugin,
            events,
            frames,
            offline,
            hasOwnInputEvents,
            inputAudio,
            cancellationToken
        );

        try
        {
            await _checkpointRepository.CommitAsync(
                plugin,
                _workerIdentity.WorkerId,
                epoch,
                seq,
                result,
                deliveries
            );
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    private async Task<PluginBlockProcessResult> ProcessBlockUnderLeaseAsync(
        PluginId pluginId,
        Plugin plugin,
        IReadOnlyList<PluginEvent> events,
        int frames,
        bool offline,
        bool hasOwnInputEvents,
        ReadOnlyMemory<float>? inputAudio,
        CancellationToken cancellationToken
    )
    {
        await using IPluginEngineLease engineLease = await _pluginEnginePool.AcquireAsync(
            plugin.Descriptor.Name,
            plugin.SampleRate,
            plugin.BlockSize,
            plugin.Channels,
            cancellationToken
        );
        IPluginEngine engine = engineLease.Engine;

        PluginProcessingMode pluginProcessingMode = offline ? PluginProcessingMode.Offline : PluginProcessingMode.Realtime;
        if (plugin.ProcessingMode != pluginProcessingMode)
        {
            plugin.ProcessingMode = pluginProcessingMode;
        }

        ApplyDefaultParameters(plugin);

        if (engine.SampleRate != plugin.SampleRate ||
            engine.BlockSize != plugin.BlockSize ||
            engine.Channels != plugin.Channels ||
            engine.ProcessingMode != plugin.ProcessingMode)
        {
            bool reconfigured = engine.Reconfigure(
                plugin.SampleRate,
                plugin.BlockSize,
                plugin.Channels,
                offline
            );

            if (!reconfigured)
            {
                throw new InvalidOperationException(
                    $"VST reconfigure failed. PluginId={pluginId}, PluginName={plugin.Descriptor.Name}"
                );
            }
        }

        engine.SetStateIfChanged(plugin.State);

        ApplyKnownParameters(engine, plugin);

        ProcessedAudio processedAudio = ProcessWithEvents(
            engine,
            plugin,
            events,
            frames,
            inputAudio
        );

        try
        {
            bool isSilent = _silenceDetector.IsSilent(
                processedAudio.Audio,
                SilenceThreshold
            );

            plugin.MarkAudioActivity(
                isSilent,
                RequiredSilentBlocks
            );

            plugin.State = engine.GetState();

            bool shouldSend = offline || hasOwnInputEvents || inputAudio.HasValue || plugin.HasActiveAudio() || !isSilent;
            if (!shouldSend)
            {
                processedAudio.Dispose();

                return new PluginBlockProcessResult(
                    ReadOnlyMemory<float>.Empty,
                    ShouldSend: false
                );
            }

            return new PluginBlockProcessResult(
                processedAudio.Audio,
                shouldSend,
                processedAudio.Lease
            );
        }
        catch
        {
            processedAudio.Dispose();
            throw;
        }
    }

    private static void ApplyKnownParameters(
        IPluginEngine engine,
        Plugin plugin
    )
    {
        foreach ((uint parameterId, float value) in plugin.Parameters)
        {
            engine.SetParameterIfChanged(parameterId, value);
        }
    }

    private void ApplyDefaultParameters(Plugin plugin)
    {
        if (plugin.Parameters.Count > 0)
        {
            return;
        }

        IReadOnlyDictionary<uint, float>? defaults = _defaultParameterProvider.GetDefaults(
            plugin.Descriptor.Name
        );

        if (defaults is not null)
        {
            plugin.Parameters = new Dictionary<uint, float>(defaults);
        }
    }

    private ProcessedAudio ProcessWithEvents(
        IPluginEngine engine,
        Plugin plugin,
        IReadOnlyList<PluginEvent> events,
        int frames,
        ReadOnlyMemory<float>? inputAudio
    )
    {
        if (plugin.Descriptor.Type == PluginType.EFFECT)
        {
            foreach (PluginEvent pluginEvent in events)
            {
                _eventApplier.Apply(engine, plugin, pluginEvent);
            }

            ReadOnlySpan<float> effectInput = inputAudio.HasValue
                ? inputAudio.Value.Span
                : ReadOnlySpan<float>.Empty;

            return CopyEngineOutput(
                engine.Process(effectInput, frames),
                checked(frames * engine.Channels)
            );
        }

        if (events.Count == 0)
        {
            return CopyEngineOutput(
                engine.Process(frames),
                checked(frames * engine.Channels)
            );
        }

        int channels = engine.Channels;
        int samples = checked(frames * channels);
        PooledAudioBuffer outBuffer = PooledAudioBuffer.Rent(samples);
        outBuffer.Buffer.AsSpan(0, outBuffer.Length).Clear();

        int cursor = 0;

        foreach (PluginEvent pluginEvent in events)
        {
            int offset = Math.Clamp(
                pluginEvent.Offset,
                0,
                Math.Max(0, frames - 1)
            );

            int len = Math.Max(0, offset - cursor);

            if (len > 0)
            {
                CopyAudioPart(
                    engine.Process(len),
                    outBuffer.Buffer,
                    cursor,
                    channels
                );

                cursor += len;
            }

            _eventApplier.Apply(engine, plugin, pluginEvent);
        }

        if (cursor < frames)
        {
            CopyAudioPart(
                engine.Process(frames - cursor),
                outBuffer.Buffer,
                cursor,
                channels
            );
        }

        return new ProcessedAudio(outBuffer.Memory, outBuffer);
    }

    private static ProcessedAudio CopyEngineOutput(
        ReadOnlyMemory<float> source,
        int expectedSamples
    )
    {
        if (source.Length > expectedSamples)
        {
            throw new InvalidOperationException(
                $"VST returned too many audio samples. Expected={expectedSamples}, Actual={source.Length}."
            );
        }

        PooledAudioBuffer buffer = PooledAudioBuffer.Rent(expectedSamples);
        Span<float> target = buffer.Buffer.AsSpan(0, buffer.Length);
        target.Clear();
        source.Span.CopyTo(target);
        return new ProcessedAudio(buffer.Memory, buffer);
    }

    private static void CopyAudioPart(
        ReadOnlyMemory<float> source,
        float[] target,
        int frameOffset,
        int channels
    )
    {
        source.Span.CopyTo(target.AsSpan(frameOffset * channels));
    }

    private readonly record struct ProcessedAudio(
        ReadOnlyMemory<float> Audio,
        IDisposable? Lease = null
    ) : IDisposable
    {
        public void Dispose()
        {
            Lease?.Dispose();
        }
    }

    private sealed class PooledAudioBuffer : IDisposable
    {
        private float[]? _buffer;

        private PooledAudioBuffer(float[] buffer, int length)
        {
            _buffer = buffer;
            Length = length;
        }

        public float[] Buffer => _buffer
            ?? throw new ObjectDisposedException(nameof(PooledAudioBuffer));

        public int Length { get; }

        public ReadOnlyMemory<float> Memory => Buffer.AsMemory(0, Length);

        public static PooledAudioBuffer Rent(int length)
        {
            return new PooledAudioBuffer(
                ArrayPool<float>.Shared.Rent(length),
                length
            );
        }

        public void Dispose()
        {
            float[]? buffer = _buffer;
            if (buffer is null)
            {
                return;
            }

            _buffer = null;
            ArrayPool<float>.Shared.Return(buffer);
        }
    }
}
