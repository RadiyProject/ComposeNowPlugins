using System.Buffers;
using ComposeNowPlugins.Cache;
using ComposeNowPlugins.Configurations;
using ComposeNowPlugins.Models;
using ComposeNowPlugins.Models.Ids;
using ComposeNowPlugins.Repositories.Plugins;
using ComposeNowPlugins.Wrappers;

namespace ComposeNowPlugins.Services.Processing;

public sealed class PluginBlockProcessor(
    ICache cache,
    IVstEngineFactory vstEngineFactory,
    IPluginRepository pluginRepository,
    IPluginEventRepository pluginEventRepository,
    IPluginProcessingGate processingGate,
    ILogger<PluginBlockProcessor> logger
) : IPluginBlockProcessor
{
    private readonly ICache _cache = cache;
    private readonly IVstEngineFactory _vstEngineFactory = vstEngineFactory;
    private readonly IPluginRepository _pluginRepository = pluginRepository;
    private readonly IPluginEventRepository _pluginEventRepository = pluginEventRepository;
    private readonly IPluginProcessingGate _processingGate = processingGate;
    private readonly ILogger<PluginBlockProcessor> _logger = logger;

    private static readonly TimeSpan LockTtl = TimeSpan.FromSeconds(10);

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

        List<(PluginEvent Event, int Index)> sortableEvents = new(controlEvents.Count + blockEvents.Count);
        AddPluginEvents(sortableEvents, controlEvents, pluginId);
        AddPluginEvents(sortableEvents, blockEvents, pluginId);

        sortableEvents.Sort(static (left, right) =>
        {
            int byOffset = left.Event.Offset.CompareTo(right.Event.Offset);
            if (byOffset != 0) return byOffset;

            int byPriority = EventPriority(left.Event).CompareTo(EventPriority(right.Event));
            return byPriority != 0
                ? byPriority
                : left.Index.CompareTo(right.Index);
        });

        List<PluginEvent> events = new(sortableEvents.Count);
        foreach ((PluginEvent pluginEvent, _) in sortableEvents)
        {
            events.Add(pluginEvent);
        }

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

    private static void AddPluginEvents(
        List<(PluginEvent Event, int Index)> target,
        IReadOnlyList<PluginEvent> source,
        PluginId pluginId
    )
    {
        for (int i = 0; i < source.Count; i++)
        {
            PluginEvent pluginEvent = source[i];
            if (pluginEvent.PluginId.GetValue() == pluginId.GetValue())
            {
                target.Add((pluginEvent, target.Count));
            }
        }
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

        bool isSilent = IsSilent(
            processedAudio.Audio,
            threshold: 0.00001f
        );

        plugin.MarkAudioActivity(
            isSilent,
            requiredSilentBlocks: 16
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

    private static void ApplyDefaultParameters(Plugin plugin)
    {
        if (plugin.Parameters.Count > 0)
        {
            return;
        }

        Dictionary<uint, float>? defaults = plugin.Descriptor.Name switch
        {
            "Delay" => new Dictionary<uint, float>
            {
                [100] = 0.32f,
                [101] = 0.35f,
                [102] = 0.35f,
                [103] = 0.0f
            },
            "Reverb" => new Dictionary<uint, float>
            {
                [100] = 0.55f,
                [101] = 0.35f,
                [102] = 0.65f,
                [103] = 0.8f
            },
            _ => null
        };

        if (defaults is not null)
        {
            plugin.SetParameters(defaults);
        }
    }

    private static ProcessedAudio ProcessWithEvents(
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
                ApplyEvent(vst, plugin, pluginEvent);
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

            ApplyEvent(vst, plugin, pluginEvent);
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

    private static void ApplyEvent(
        VstEngine vst,
        Plugin plugin,
        PluginEvent pluginEvent
    )
    {
        switch (pluginEvent.Type)
        {
            case PluginEventType.NoteOn:
                if (pluginEvent.Pitch.HasValue)
                {
                    vst.NoteOn(
                        pluginEvent.Pitch.Value,
                        pluginEvent.Velocity ?? 1f
                    );

                    plugin.MarkNoteOn(pluginEvent.Pitch.Value);
                }
                break;

            case PluginEventType.NoteOff:
                if (pluginEvent.Pitch.HasValue)
                {
                    vst.NoteOff(pluginEvent.Pitch.Value);

                    plugin.MarkNoteOff(pluginEvent.Pitch.Value);
                }
                break;

            case PluginEventType.Param:
                if (pluginEvent.ParameterId.HasValue &&
                    pluginEvent.ParameterValue.HasValue)
                {
                    vst.SetParamIfChanged(
                        pluginEvent.ParameterId.Value,
                        pluginEvent.ParameterValue.Value
                    );

                    plugin.SetParameter(
                        pluginEvent.ParameterId.Value,
                        pluginEvent.ParameterValue.Value
                    );
                }
                break;

            case PluginEventType.Panic:
                for (int n = 0; n < 128; n++)
                {
                    vst.NoteOff(n);
                }

                plugin.Panic();
                break;
        }
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

    private static bool IsSilent(
        ReadOnlyMemory<float> audio,
        float threshold = 0.00001f
    )
    {
        ReadOnlySpan<float> span = audio.Span;

        for (int i = 0; i < span.Length; i++)
        {
            if (Math.Abs(span[i]) > threshold)
            {
                return false;
            }
        }

        return true;
    }

    private static int EventPriority(PluginEvent pluginEvent)
    {
        if (!pluginEvent.Seq.HasValue)
        {
            return 0;
        }

        return pluginEvent.Type switch
        {
            PluginEventType.Panic => 0,
            PluginEventType.NoteOff => 1,
            PluginEventType.Param => 2,
            PluginEventType.NoteOn => 3,
            _ => 10
        };
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
