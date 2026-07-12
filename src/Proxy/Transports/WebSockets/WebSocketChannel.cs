using System.Buffers;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;

namespace ComposeNowPlugins.Proxy.Transports.WebSockets;

public class WebSocketChannel(WebSocket ws) : IRuntimeChannel
{
    private readonly WebSocket _ws = ws;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    public string Transport => "websocket";

    public async IAsyncEnumerable<IncomingMessage> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buf = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buf), cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close) yield break;
                    if (result.Count > 0) ms.Write(buf, 0, result.Count);
                } while (!result.EndOfMessage);

                string mt = result.MessageType == WebSocketMessageType.Text
                    ? "text/plain" 
                    : "application/octet-stream";
                yield return new IncomingMessage(ms.GetBuffer().AsMemory(0, (int)ms.Length), mt, EndOfMessage: true);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> payload, string? contentType, bool endOfMessage, CancellationToken cancellationToken)
    {
        WebSocketMessageType type = contentType == "text/plain"
            ? WebSocketMessageType.Text
            : WebSocketMessageType.Binary;

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (_ws.State != WebSocketState.Open)
            {
                return;
            }

            await _ws.SendAsync(
                payload,
                type,
                endOfMessage,
                cancellationToken
            );
        }
        finally
        {
            _sendLock.Release();
        }
    }
}
