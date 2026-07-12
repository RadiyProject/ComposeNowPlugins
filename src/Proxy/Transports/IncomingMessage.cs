namespace ComposeNowPlugins.Transports;

public readonly record struct IncomingMessage(
    ReadOnlyMemory<byte> Payload,
    string ContentType,               // "application/octet-stream", "text/plain" ...
    bool EndOfMessage = true          // на случай фрагментации
);