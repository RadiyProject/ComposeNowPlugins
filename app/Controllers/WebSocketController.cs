using ComposeNowPlugins.Transports;
using Microsoft.AspNetCore.Mvc;

namespace ComposeNowPlugins.Controllers;

[ApiController]
public class WebSocketController(IRealtimeTransport transport) : ControllerBase
{
    private readonly IRealtimeTransport _transport = transport;

    [Route("/ws")]
    public Task Connect() => _transport.ConnectAsync(HttpContext, HttpContext.RequestAborted);
}