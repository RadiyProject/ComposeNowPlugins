namespace ComposeNowPlugins.Services.Processing;

public sealed record PluginBlockProcessResult(
    ReadOnlyMemory<float> Audio,
    bool ShouldSend
);