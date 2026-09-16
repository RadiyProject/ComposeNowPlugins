namespace ComposeNowPlugins.Application.Services.Processing;

public interface IPluginWorkerIdentity
{
    string WorkerId { get; }
    Uri Address { get; }
}
