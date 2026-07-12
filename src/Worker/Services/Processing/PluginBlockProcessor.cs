using System.Buffers;
using ComposeNowPlugins.Configurations;
using ComposeNowPlugins.Models;
using ComposeNowPlugins.Models.Ids;
using ComposeNowPlugins.Repositories.Plugins;
using ComposeNowPlugins.Wrappers;

namespace ComposeNowPlugins.Services.Processing;

public sealed class PluginBlockProcessor(
    IVstEngineFactory vstEngineFactory,
    IPluginRepository pluginRepository,
    IPluginEventRepository pluginEventRepository,
    IPluginProcessingGate processingGate,
    IPluginEventMerger eventMerger,
    IPluginDefaultParameterProvider defaultParameterProvider,
    IPluginEventApplier eventApplier,
    IAudioSilenceDetector silenceDetector
) : IPluginBlockProcessor
{
    private readonly IVstEngineFactory _vstEngineFactory = vstEngineFactory;
    private readonly IPluginRepository _pluginRepository = pluginRepository;
    private readonly IPluginEventRepository _pluginEventRepository = pluginEventRepository;
    private readonly IPluginProcessingGate _processingGate = processingGate;
    private readonly IPluginEventMerger _eventMerger = eventMerger;
    private readonly IPluginDefaultParameterProvider _defaultParameterProvider = defaultParameterProvider;
    private readonly IPluginEventApplier _eventApplier = eventApplier;
    private readonly IAudioSilenceDetector _silenceDetector = silenceDetector;

    private const float SilenceThreshold = 0.00001f;
    private const int RequiredSilentBlocks = 16;

    public async Task<PluginBlockProcessResult> ProcessBlockAsync(
        PluginId pluginId,
        ulong seq,
        int frames,
        bool offline,
        ReadOnlyMemory<float>? inputAudio,
        CancellationToken cancellationToken
    )
    {
        Plugin plugin = await _pluginRepository.GetAsync(pluginId)
            ?? throw new InvalidOperationException($"Plugin state was not found. PluginId={pluginId}");

        IReadOnlyList<PluginEvent> controlEvents =
            await _pluginEventRepository.PopControlEventsAsync(pluginId);

        IReadOnlyList<PluginEvent> blockEvents =
            await _pluginEventRepository.GetBlockEventsAsync(pluginId, seq);

        IReadOnlyList<PluginEvent> events = _eventMerger.Merge(
            pluginId,
            controlEvents,
            blockEvents
        );

        bool hasOwnInputEvents = events.Count > 0;
        bool hasOwnActiveAudio = plugin.HasActiveAudio();
        bool hasInputAudio = inputAudio.HasValue && inputAudio.Value.Length > 0;

        if (!offline && !hasOwnInputEvents && !hasOwnActiveAudio && !hasInputAudio)
        {
            await _pluginEventRepository.DeleteBlockEventsAsync(pluginId, seq);

            return new PluginBlockProcessResult(
                ReadOnlyMemory<float>.Empty,
                ShouldSend: true
            );
        }

        return await _processingGate.RunAsync(
            plugin.Descriptor.Name,
            async () =>
            {
                PluginBlockProcessResult result = await ProcessBlockUnderGateAsync(
                    pluginId,
                    plugin,
                    events,
                    seq,
                    frames,
                    offline,
                    hasOwnInputEvents,
                    hasOwnActiveAudio,
                    inputAudio
                );

                await _pluginEventRepository.DeleteBlockEventsAsync(pluginId, seq);

                return result;
            },
            cancellationToken
        );
    }

    private async Task<PluginBlockProcessResult> ProcessBlockUnderGateAsync(
        PluginId pluginId,
        Plugin plugin,
        IReadOnlyList<PluginEvent> events,
        ulong seq,
        int frames,
        bool offline,
        bool hasOwnInputEvents,
        bool hasOwnActiveAudio,
        ReadOnlyMemory<float>? inputAudio
    )
    {
        VstEngine vst = _vstEngineFactory.Create(
            pluginId,
            plugin.Descriptor.Name,
            plugin.SampleRate,
            plugin.BlockSize,
            plugin.Channels
        );

        PluginProcessingMode pluginProcessingMode = offline ? PluginProcessingMode.Offline : PluginProcessingMode.Realtime;
        if (plugin.ProcessingMode != pluginProcessingMode)
        {
            plugin.SetProcessingMode(pluginProcessingMode);
        }

        ApplyDefaultParameters(plugin);

        if (vst.SampleRate != plugin.SampleRate ||
            vst.BlockSize != plugin.BlockSize ||
            vst.Channels != plugin.Channels ||
            vst.ProcessingMode != plugin.ProcessingMode)
        {
            bool reconfigured = vst.Reconfigure(
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

        vst.SetStateIfChanged(plugin.State);

        ApplyKnownParameters(vst, plugin);

        ProcessedAudio processedAudio = ProcessWithEvents(
            vst,
            plugin,
            events,
            frames,
            inputAudio
        );

        bool isSilent = _silenceDetector.IsSilent(
            processedAudio.Audio,
            SilenceThreshold
        );

        plugin.MarkAudioActivity(
            isSilent,
            RequiredSilentBlocks
        );

        plugin.SetState(vst.GetState());

        await _pluginRepository.UpdateAsync(
            pluginId,
            plugin
        );

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

    private static void ApplyKnownParameters(
        VstEngine vst,
        Plugin plugin
    )
    {
        foreach ((uint parameterId, float value) in plugin.Parameters)
        {
            vst.SetParamIfChanged(parameterId, value);
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
            plugin.SetParameters(new Dictionary<uint, float>(defaults));
        }
    }

    private ProcessedAudio ProcessWithEvents(
        VstEngine vst,
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
                _eventApplier.Apply(vst, plugin, pluginEvent);
            }

            ReadOnlySpan<float> effectInput = inputAudio.HasValue
                ? inputAudio.Value.Span
                : ReadOnlySpan<float>.Empty;

            return new ProcessedAudio(vst.Process(effectInput, frames));
        }

        if (events.Count == 0)
        {
            return new ProcessedAudio(vst.Process(frames));
        }

        int channels = vst.Channels;
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
                    vst.Process(len),
                    outBuffer.Buffer,
                    cursor,
                    channels
                );

                cursor += len;
            }

            _eventApplier.Apply(vst, plugin, pluginEvent);
        }

        if (cursor < frames)
        {
            CopyAudioPart(
                vst.Process(frames - cursor),
                outBuffer.Buffer,
                cursor,
                channels
            );
        }

        return new ProcessedAudio(outBuffer.Memory, outBuffer);
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
