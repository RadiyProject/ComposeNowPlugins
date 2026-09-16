using ComposeNowPlugins.Domain.Models;

namespace ComposeNowPlugins.Application.Repositories.Plugins;

public sealed record PluginEventDelivery(
    string StreamKey,
    string EntryId,
    PluginEvent Event
);
