using ComposeNowPlugins.Domain.Configurations;
using ComposeNowPlugins.Domain.Models;
using ComposeNowPlugins.Domain.Models.Ids;

namespace ComposeNowPlugins.Worker.Services.Processing;

public sealed class PluginEventMerger : IPluginEventMerger
{
    public IReadOnlyList<PluginEvent> Merge(
        PluginId pluginId,
        IReadOnlyList<PluginEvent> controlEvents,
        IReadOnlyList<PluginEvent> blockEvents
    )
    {
        List<(PluginEvent Event, int Index)> sortableEvents = new(
            controlEvents.Count + blockEvents.Count
        );

        AddPluginEvents(sortableEvents, controlEvents, pluginId);
        AddPluginEvents(sortableEvents, blockEvents, pluginId);

        sortableEvents.Sort(static (left, right) =>
        {
            int byOffset = left.Event.Offset.CompareTo(right.Event.Offset);
            if (byOffset != 0)
            {
                return byOffset;
            }

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

        return events;
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
