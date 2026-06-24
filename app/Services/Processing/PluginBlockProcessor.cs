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

        List<PluginEvent> events = [..controlEvents, ..blockEvents];

        events = [.. events.Where(item => BelongsToPlugin(item, pluginId))];

        events = [.. events
            .Select((item, index) => new { Event = item, Index = index })
            .OrderBy(item => item.Event.Offset)
            .ThenBy(item => EventPriority(item.Event))
            .ThenBy(item => item.Index)
            .Select(item => item.Event)];

        bool hasOwnInputEvents = events.Count > 0;
        bool hasOwnActiveAudio = plugin.HasActiveAudio();
        bool hasInputAudio = inputAudio.HasValue && inputAudio.Value.Length > 0;

        if (!offline && !hasOwnInputEvents && !hasOwnActiveAudio && !hasInputAudio)
        {
            await _pluginEventRepository.DeleteBlockEventsAsync(pluginId, seq);

            return new PluginBlockProcessResult(
                new float[frames * Math.Max(1, plugin.Channels)],
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

    private static bool BelongsToPlugin(
        PluginEvent pluginEvent,
        PluginId pluginId
    )
    {
        return pluginEvent.PluginId.GetValue() == pluginId.GetValue();
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

        vst.SetState(plugin.State);

        ApplyKnownParameters(vst, plugin);

        ReadOnlyMemory<float> audio = ProcessWithEvents(
            vst,
            plugin,
            events,
            frames,
            inputAudio
        );

        bool isSilent = IsSilent(
            audio,
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
            return new PluginBlockProcessResult(
                ReadOnlyMemory<float>.Empty,
                ShouldSend: false
            );
        }

        return new PluginBlockProcessResult(
            audio,
            shouldSend
        );
    }

    private static void ApplyKnownParameters(
        VstEngine vst,
        Plugin plugin
    )
    {
        foreach ((uint parameterId, float value) in plugin.Parameters)
        {
            vst.SetParam(parameterId, value);
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

    private static ReadOnlyMemory<float> ProcessWithEvents(
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

            ReadOnlyMemory<float> effectInput = inputAudio.HasValue
                ? inputAudio.Value
                : new ReadOnlyMemory<float>(new float[frames * Math.Max(1, vst.Channels)]);

            return vst.Process(effectInput.Span, frames).ToArray();
        }

        if (events.Count == 0)
        {
            return vst.Process(frames).ToArray();
        }

        int channels = vst.Channels;
        float[] outBuffer = new float[frames * channels];

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
                    outBuffer,
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
                outBuffer,
                cursor,
                channels
            );
        }

        return outBuffer;
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
                    vst.SetParam(
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
        float[] sourceArray = source.ToArray();

        Buffer.BlockCopy(
            sourceArray,
            0,
            target,
            frameOffset * channels * sizeof(float),
            sourceArray.Length * sizeof(float)
        );
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
}
