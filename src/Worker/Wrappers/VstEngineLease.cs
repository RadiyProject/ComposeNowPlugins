using ComposeNowPlugins.Application.Services.Processing;

namespace ComposeNowPlugins.Worker.Wrappers;

public sealed class VstEngineLease : IPluginEngineLease
{
    private Func<ValueTask>? _release;

    internal VstEngineLease(VstEngine engine, Func<ValueTask> release)
    {
        Engine = engine;
        _release = release;
    }

    public IPluginEngine Engine { get; }

    public ValueTask DisposeAsync()
    {
        Func<ValueTask>? release = Interlocked.Exchange(ref _release, null);
        return release is null ? ValueTask.CompletedTask : release();
    }
}
