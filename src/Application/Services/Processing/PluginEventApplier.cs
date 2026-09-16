using ComposeNowPlugins.Domain.Configurations;
using ComposeNowPlugins.Domain.Models;

namespace ComposeNowPlugins.Application.Services.Processing;

public sealed class PluginEventApplier : IPluginEventApplier
{
    public void Apply(
        IPluginEngine engine,
        Plugin plugin,
        PluginEvent pluginEvent
    )
    {
        switch (pluginEvent.Type)
        {
            case PluginEventType.NoteOn:
                if (pluginEvent.Pitch.HasValue)
                {
                    engine.NoteOn(
                        pluginEvent.Pitch.Value,
                        pluginEvent.Velocity ?? 1f
                    );

                    plugin.MarkNoteOn(pluginEvent.Pitch.Value);
                }
                break;

            case PluginEventType.NoteOff:
                if (pluginEvent.Pitch.HasValue)
                {
                    engine.NoteOff(pluginEvent.Pitch.Value);

                    plugin.MarkNoteOff(pluginEvent.Pitch.Value);
                }
                break;

            case PluginEventType.Param:
                if (pluginEvent.ParameterId.HasValue &&
                    pluginEvent.ParameterValue.HasValue)
                {
                    engine.SetParameterIfChanged(
                        pluginEvent.ParameterId.Value,
                        pluginEvent.ParameterValue.Value
                    );

                    plugin.ChangeParameter(
                        pluginEvent.ParameterId.Value,
                        pluginEvent.ParameterValue.Value
                    );
                }
                break;

            case PluginEventType.Panic:
                for (int note = 0; note < 128; note++)
                {
                    engine.NoteOff(note);
                }

                plugin.Panic();
                break;
        }
    }
}
