using ComposeNowPlugins.Domain.Configurations;
using ComposeNowPlugins.Domain.Models;
using ComposeNowPlugins.Worker.Wrappers;

namespace ComposeNowPlugins.Worker.Services.Processing;

public sealed class PluginEventApplier : IPluginEventApplier
{
    public void Apply(
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
                for (int note = 0; note < 128; note++)
                {
                    vst.NoteOff(note);
                }

                plugin.Panic();
                break;
        }
    }
}
