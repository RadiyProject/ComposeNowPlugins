namespace ComposeNowPlugins.Services.Processing;

public sealed record PluginBlockProcessResult(
    ReadOnlyMemory<float> Audio,
    bool ShouldSend,
    IDisposable? Lease = null
) : IDisposable
{
    public void Dispose()
    {
        Lease?.Dispose();
    }
}
