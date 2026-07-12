using System.Text;
using ComposeNowPlugins.Application.Exceptions;
using ComposeNowPlugins.Application.Repositories.Plugins;
using ComposeNowPlugins.Domain.Models.Ids;

namespace ComposeNowPlugins.Proxy.Transports;

public sealed class PluginCacheLeaseRenewer(
    PluginId pluginId,
    IPluginRepository pluginRepository,
    ILogger logger
)
{
    private static readonly TimeSpan PluginCacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PluginCacheRenewBefore = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PluginCacheRenewResponseTimeout = TimeSpan.FromSeconds(10);

    private readonly PluginId _pluginId = pluginId;
    private readonly IPluginRepository _pluginRepository = pluginRepository;
    private readonly ILogger _logger = logger;
    private readonly Lock _lock = new();

    private string? _expectedToken;
    private TaskCompletionSource<string>? _confirmation;

    public bool TryHandleConfirmation(string text)
    {
        const string prefix = "plugin cache renew ok ";

        if (!text.StartsWith(
            prefix,
            StringComparison.OrdinalIgnoreCase
        ))
        {
            return false;
        }

        string token = text[prefix.Length..].Trim();

        if (string.IsNullOrWhiteSpace(token))
        {
            return true;
        }

        TaskCompletionSource<string>? confirmation = null;

        lock (_lock)
        {
            if (_expectedToken == token)
            {
                confirmation = _confirmation;
                _expectedToken = null;
                _confirmation = null;
            }
        }

        confirmation?.TrySetResult(token);

        return true;
    }

    public async Task RunAsync(
        IRuntimeChannel channel,
        CancellationTokenSource sessionCts
    )
    {
        CancellationToken cancellationToken = sessionCts.Token;

        TimeSpan delay = PluginCacheTtl - PluginCacheRenewBefore;

        if (delay <= TimeSpan.Zero)
        {
            delay = TimeSpan.FromSeconds(30);
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(
                    delay,
                    cancellationToken
                );

                string token = Guid.NewGuid().ToString("N");

                var confirmation = new TaskCompletionSource<string>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );

                lock (_lock)
                {
                    _expectedToken = token;
                    _confirmation = confirmation;
                }

                await channel.SendAsync(
                    Encoding.UTF8.GetBytes($"plugin cache renew {token}"),
                    "text/plain",
                    endOfMessage: true,
                    cancellationToken
                );

                Task completed = await Task.WhenAny(
                    confirmation.Task,
                    Task.Delay(
                        PluginCacheRenewResponseTimeout,
                        cancellationToken
                    )
                );

                if (completed != confirmation.Task)
                {
                    _logger.LogWarning(
                        "Plugin cache renew confirmation timeout. Closing websocket session. PluginId={PluginId}",
                        _pluginId
                    );

                    await sessionCts.CancelAsync();
                    return;
                }

                await confirmation.Task;

                try
                {
                    await _pluginRepository.RefreshTtlAsync(_pluginId);
                }
                catch (EntityNotFoundException exception)
                {
                    _logger.LogWarning(
                        exception,
                        "Plugin cache TTL refresh failed. Closing websocket session. PluginId={PluginId}",
                        _pluginId
                    );

                    await sessionCts.CancelAsync();
                    return;
                }

                _logger.LogDebug(
                    "Plugin cache TTL refreshed. PluginId={PluginId}",
                    _pluginId
                );

                lock (_lock)
                {
                    if (_expectedToken == token)
                    {
                        _expectedToken = null;
                        _confirmation = null;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal session shutdown.
        }
        finally
        {
            lock (_lock)
            {
                _expectedToken = null;
                _confirmation = null;
            }
        }
    }
}
