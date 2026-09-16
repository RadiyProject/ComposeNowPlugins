namespace ComposeNowPlugins.Application.Services.Processing;

public sealed record PluginWorkerEndpoint(
    string WorkerId,
    Uri Address
);
