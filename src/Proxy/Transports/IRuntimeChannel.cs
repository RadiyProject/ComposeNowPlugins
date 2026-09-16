namespace ComposeNowPlugins.Proxy.Transports;

public interface IRuntimeChannel
{
    // Read incoming messages until the channel closes.
    IAsyncEnumerable<IncomingMessage> ReadAllAsync(CancellationToken cancellationToken);

    // Send a response with an optional content type.
    ValueTask SendAsync(ReadOnlyMemory<byte> payload, string? contentType, bool endOfMessage, CancellationToken cancellationToken);

    // Channel metadata, such as the peer and protocol.
    string Transport { get; } // "websocket", "grpc" ...
}
