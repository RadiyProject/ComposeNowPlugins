namespace ComposeNowPlugins.Configurations;

public sealed class PluginWorkerOptions
{
    public const string SectionName = "PluginWorker";

    public string Address { get; init; } = "http://worker:5002";
}
