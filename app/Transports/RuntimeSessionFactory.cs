namespace ComposeNowPlugins.Transports;

public sealed class RuntimeSessionFactory(IServiceProvider serviceProvider) : IRuntimeSessionFactory
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;

    public IRuntimeSession Create(HttpContext context)
    {
        var mode = context.Request.Query["mode"].ToString();
        return mode == "audio"
            ? _serviceProvider.GetRequiredService<AudioRuntimeSession>()
            : _serviceProvider.GetRequiredService<EchoRuntimeSession>();
    }
}