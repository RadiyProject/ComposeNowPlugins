namespace ComposeNowPlugins.Proxy.Transports;

public sealed class MessageTooLargeException(string message) : Exception(message);
