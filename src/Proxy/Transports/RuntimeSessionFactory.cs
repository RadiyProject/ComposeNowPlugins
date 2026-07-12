using ComposeNowPlugins.Domain.Models.Ids;

namespace ComposeNowPlugins.Proxy.Transports;

public sealed class RuntimeSessionFactory(IServiceProvider serviceProvider,
    ILogger<RuntimeSessionFactory> logger) : IRuntimeSessionFactory
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ILogger<RuntimeSessionFactory> _logger = logger;

    public IRuntimeSession Create(HttpContext context)
    {
        string mode = context.Request.Query["mode"].ToString();
        _logger.LogDebug(
            "Runtime session request. Path={Path}, Query={Query}, Mode={Mode}, PluginId={PluginId}",
            context.Request.Path,
            context.Request.QueryString,
            mode,
            context.Request.Query["pluginId"].ToString()
        );
        if (mode != "audio")
        {
            return _serviceProvider.GetRequiredService<EchoRuntimeSession>();
        }

        string pluginName = context.Request.Query["plugin"].ToString();
        if (string.IsNullOrWhiteSpace(pluginName))
        {
            throw new FormatException($"Plugin name {pluginName} is incorrect or blank.");
        }

        string pluginIdValue = context.Request.Query["pluginId"].ToString();
        PluginId pluginId = string.IsNullOrWhiteSpace(pluginIdValue)
            ? PluginId.New()
            : new PluginId(pluginIdValue);

        return ActivatorUtilities.CreateInstance<AudioRuntimeSession>(
            _serviceProvider,
            pluginId,
            pluginName
        );
    }
}
