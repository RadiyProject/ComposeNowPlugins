using ComposeNowPlugins.Services.Cluster;
using ComposeNowPlugins.Middleware;

namespace ComposeNowPlugins.Extensions;

public static class WebApplicationExtensions
{
    public static WebApplication UseApplicationPipeline(
        this WebApplication app
    )
    {
        app.UseMiddleware<ExceptionHandlingMiddleware>();

        app.MapGet("/healthcheck", (IPluginNodeState nodeState) =>
        {
            if (nodeState.IsDraining)
            {
                return (IResult)Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            return (IResult)Results.Ok("Everything work's fine");
        });

        app.UseRouting();

        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromMinutes(2)
        });

        app.MapControllers();

        return app;
    }
}
