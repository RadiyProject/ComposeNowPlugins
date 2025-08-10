using System.Diagnostics;

namespace ComposeNowPlugins.Transports;

public sealed class LoggingTransport(IRealtimeTransport inner, ILogger<LoggingTransport> log) : IRealtimeTransport
{
    private readonly IRealtimeTransport _inner = inner;
    private readonly ILogger<LoggingTransport> _log = log;

    public async Task ConnectAsync(HttpContext ctx, CancellationToken ct)
    {
        _log.LogInformation("RT connect {Path}", ctx.Request.Path);
        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.ConnectAsync(ctx, ct);
            _log.LogInformation("RT done {Elapsed} ms", sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "RT error");
            throw;
        }
    }
}
