using System.Net.WebSockets;

namespace ComposeNowPlugins.Transports.WebSockets;

public class WebSocketTransport : IRealtimeTransport
{
    public async Task ConnectAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        try
        {
            // передаём токен отмены от Kestrel, чтобы сервер аккуратно завершался при разрыве соединения
            await Echo(webSocket, context.RequestAborted);
        }
        catch (WebSocketException ex)
        {
            // клиент мог закрыться без рукопожатия — не считаем это аварией
            Console.WriteLine($"WS warning: {ex.Message}");
        }
        // using сам закроет, но если вдруг мы всё ещё открыты — отправим финальный close
        finally
        {
            if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try { await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", cancellationToken); }
                catch { /* ignore */ }
            }
        }
    }
    
    private static async Task Echo(WebSocket webSocket, CancellationToken ct)
    {
        var buffer = new byte[4 * 1024];

        while (!ct.IsCancellationRequested)
        {
            WebSocketReceiveResult receiveResult;
            try
            {
                receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            }
            catch (OperationCanceledException)
            {
                break; // запрос отменён (соединение порвали)
            }
            catch (WebSocketException)
            {
                break; // клиент закрылся/ошибка транспорта — выходим без падения
            }

            if (receiveResult.CloseStatus.HasValue)
            {
                // пришёл Close от клиента — отвечаем Close и выходим
                await webSocket.CloseAsync(receiveResult.CloseStatus.Value, receiveResult.CloseStatusDescription, CancellationToken.None);
                break;
            }

            if (receiveResult.Count > 0)
            {
                await webSocket.SendAsync(
                    new ArraySegment<byte>(buffer, 0, receiveResult.Count),
                    receiveResult.MessageType,
                    receiveResult.EndOfMessage,
                    CancellationToken.None);
            }
        }
    }
}