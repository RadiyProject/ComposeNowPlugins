namespace ComposeNowPlugins.Extensions;

public static class WebApplicationExtensions
{
    public static WebApplication UseApplicationPipeline(
        this WebApplication app
    )
    {
        app.MapGet("/healthcheck", () => "Everything work's fine");

        app.UseRouting();

        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromMinutes(2)
        });

        app.MapControllers();

        return app;
    }
}