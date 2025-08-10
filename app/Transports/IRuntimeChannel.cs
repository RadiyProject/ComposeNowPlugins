namespace ComposeNowPlugins.Transports;

public interface IRuntimeChannel
{
    // читать все входящие сообщения до закрытия
    IAsyncEnumerable<IncomingMessage> ReadAllAsync(CancellationToken cancellationToken);

    // отправить ответ (контент-тип опционально)
    ValueTask SendAsync(ReadOnlyMemory<byte> payload, string? contentType, bool endOfMessage, CancellationToken cancellationToken);

    // метаданные канала (peer, протокол и пр.) — по вкусу
    string Transport { get; } // "websocket", "grpc" ...
}