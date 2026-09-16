using System.Net.WebSockets;
using System.Text;
using ComposeNowPlugins.Infrastructure.Services.Cluster;

namespace ComposeNowPlugins.Proxy.Transports.WebSockets;

public class WebSocketTransport(
    IRuntimeSessionFactory factory,
    IPluginNodeState nodeState,
    IPluginLeaseValidator leaseValidator,
    ILogger<WebSocketTransport> log,
    IHostEnvironment environment
) : IRealtimeTransport
{
    private readonly IRuntimeSessionFactory _factory = factory;
    private readonly IPluginNodeState _nodeState = nodeState;
    private readonly IPluginLeaseValidator _leaseValidator = leaseValidator;
    private readonly ILogger<WebSocketTransport> _log = log;
    private readonly IHostEnvironment _environment = environment;

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
                await TryCloseOutputAsync(
                    webSocket,
                    WebSocketCloseStatus.PolicyViolation,
                    "invalid lease"
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
            _log.LogDebug(ex, "WebSocket connection ended.");
        }
        catch (MessageTooLargeException exception)
        {
            _log.LogWarning(exception, "WebSocket message limit exceeded.");

            if (webSocket is not null)
            {
                await TryCloseOutputAsync(
                    webSocket,
                    WebSocketCloseStatus.MessageTooBig,
                    ToCloseDescription(exception.Message)
                );
            }
        }
        catch (Exception exception)
        {
            _log.LogError(
                exception,
                "WS session failed."
            );

            if (webSocket is not null)
            {
                await TryCloseOutputAsync(
                    webSocket,
                    WebSocketCloseStatus.InternalServerError,
                    ToCloseDescription(
                        _environment.IsDevelopment()
                            ? exception.Message
                            : "An internal server error occurred."
                    )
                );
            }
        }
        finally
        {
            if (acquired)
            {
                _nodeState.ReleaseSession();
            }

            if (webSocket is not null)
            {
                await TryCloseOutputAsync(webSocket, WebSocketCloseStatus.NormalClosure, "bye");
            }

            webSocket?.Dispose();
        }
    }

    private async Task TryCloseOutputAsync(
        WebSocket webSocket,
        WebSocketCloseStatus closeStatus,
        string description
    )
    {
        if (webSocket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
        {
            return;
        }

        try
        {
            await webSocket.CloseOutputAsync(closeStatus, description, CancellationToken.None);
        }
        catch (Exception exception) when (
            exception is WebSocketException
                or OperationCanceledException
                or ObjectDisposedException
                or InvalidOperationException
        )
        {
            _log.LogDebug(exception, "WebSocket closed before the close frame could be sent.");
        }
    }

    private static string ToCloseDescription(string message)
    {
        const int maxCloseDescriptionLength = 120;

        if (Encoding.UTF8.GetByteCount(message) <= maxCloseDescriptionLength)
        {
            return message;
        }

        var result = new StringBuilder(message.Length);
        int byteCount = 0;
        foreach (Rune rune in message.EnumerateRunes())
        {
            if (byteCount + rune.Utf8SequenceLength > maxCloseDescriptionLength)
            {
                break;
            }

            result.Append(rune.ToString());
            byteCount += rune.Utf8SequenceLength;
        }

        return result.ToString();
    }
}
