using System.Net.WebSockets;

namespace ComposeNowPlugins.Transports.WebSockets;

public class WebSocketTransport(IRuntimeSessionFactory factory, ILogger<WebSocketTransport> log) : IRealtimeTransport
{
    private readonly IRuntimeSessionFactory _factory = factory;
    private readonly ILogger<WebSocketTransport> _log = log;

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
            var channel = new WebSocketChannel(webSocket);
            var session = _factory.Create(context);   // echo/audio/…
            await session.RunAsync(channel, context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            _log.LogInformation("WS session cancelled."); // не считаем ошибкой
        }
        catch (WebSocketException ex)
        {
            // клиент мог закрыться без рукопожатия — не считаем это аварией
            _log.LogWarning(ex, "WS transport error");
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
}