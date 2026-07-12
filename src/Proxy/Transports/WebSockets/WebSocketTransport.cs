using System.Net.WebSockets;
using ComposeNowPlugins.Services.Cluster;

namespace ComposeNowPlugins.Transports.WebSockets;

public class WebSocketTransport(
    IRuntimeSessionFactory factory,
    IPluginNodeState nodeState,
    IPluginLeaseValidator leaseValidator,
    ILogger<WebSocketTransport> log
) : IRealtimeTransport
{
    private readonly IRuntimeSessionFactory _factory = factory;
    private readonly IPluginNodeState _nodeState = nodeState;
    private readonly IPluginLeaseValidator _leaseValidator = leaseValidator;
    private readonly ILogger<WebSocketTransport> _log = log;

    public async Task ConnectAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        string pluginName = context.Request.Query["plugin"].ToString();
        string leaseId = context.Request.Query["leaseId"].ToString();

        if (_nodeState.IsDraining)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsync("Plugin node is draining.", cancellationToken);
            return;
        }

        if (!_nodeState.TryAcquireSession())
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsync("Plugin node is busy.", cancellationToken);
            return;
        }

        bool acquired = true;
        WebSocket? webSocket = null;
        try
        {
            webSocket = await context.WebSockets.AcceptWebSocketAsync();

            if (!await _leaseValidator.ValidateAsync(leaseId, pluginName, context.RequestAborted))
            {
                await webSocket.CloseAsync(
                    WebSocketCloseStatus.PolicyViolation,
                    "invalid lease",
                    context.RequestAborted
                );
                return;
            }

            var channel = new WebSocketChannel(webSocket);
            var session = _factory.Create(context);   // echo/audio/…
            await session.RunAsync(channel, context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            _log.LogDebug("WS session cancelled.");
        }
        catch (WebSocketException ex)
        {
            // клиент мог закрыться без рукопожатия — не считаем это аварией
            _log.LogWarning(ex, "WS transport error");
        }
        catch (Exception exception)
        {
            _log.LogError(
                exception,
                "WS session failed."
            );

            if (webSocket?.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await webSocket.CloseAsync(
                    WebSocketCloseStatus.InternalServerError,
                    ToCloseDescription(exception.Message),
                    context.RequestAborted
                );
            }
        }
        // using сам закроет, но если вдруг мы всё ещё открыты — отправим финальный close
        finally
        {
            if (acquired)
            {
                _nodeState.ReleaseSession();
            }

            if (webSocket?.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try { await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", cancellationToken); }
                catch { /* ignore */ }
            }

            webSocket?.Dispose();
        }
    }

    private static string ToCloseDescription(string message)
    {
        const int maxCloseDescriptionLength = 120;

        if (message.Length <= maxCloseDescriptionLength)
        {
            return message;
        }

        return message[..maxCloseDescriptionLength];
    }
}
