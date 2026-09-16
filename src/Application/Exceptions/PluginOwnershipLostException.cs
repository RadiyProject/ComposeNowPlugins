namespace ComposeNowPlugins.Application.Exceptions;

public sealed class PluginOwnershipLostException : AppException
{
    public PluginOwnershipLostException(string message)
        : base(message)
    {
    }
}
