using ComposeNowPlugins.Proxy.Transports;
using Microsoft.AspNetCore.Mvc;

namespace ComposeNowPlugins.Proxy.Controllers;

[ApiController]
public class WebSocketController(IRealtimeTransport transport) : ControllerBase
{
    private readonly IRealtimeTransport _transport = transport;

    [Route("/ws")]
    public Task Connect() => _transport.ConnectAsync(HttpContext, HttpContext.RequestAborted);
}