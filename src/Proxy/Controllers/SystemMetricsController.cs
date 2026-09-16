using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace ComposeNowPlugins.Proxy.Controllers;

[ApiController]
[Route("internal/system/metrics")]
public sealed class SystemMetricsController(IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        var expected = configuration["Infrastructure:MetricsKey"]
            ?? Environment.GetEnvironmentVariable("COMPOSE_NOW_METRICS_KEY");
        string provided = Request.Headers["X-ComposeNow-Metrics-Key"].ToString();
        if (string.IsNullOrWhiteSpace(expected) || !KeysMatch(provided, expected))
        {
            return NotFound();
        }

        using var process = Process.GetCurrentProcess();
        var uptime = DateTime.UtcNow - process.StartTime.ToUniversalTime();
        var cpu = uptime.TotalMilliseconds <= 0
            ? 0
            : process.TotalProcessorTime.TotalMilliseconds / uptime.TotalMilliseconds
                / Math.Max(1, Environment.ProcessorCount) * 100;
        return Ok(new
        {
            node = Environment.MachineName,
            service = "ComposeNow Plugins",
            runtime = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST") is null ? "Docker" : "Kubernetes",
            cpuPercent = Math.Clamp(cpu, 0, 100),
            memoryMb = process.WorkingSet64 / 1024d / 1024d,
            queueLength = 0,
            uptimeSeconds = uptime.TotalSeconds,
            details = $"{process.Threads.Count} threads · {Environment.ProcessorCount} CPU"
        });
    }

    private static bool KeysMatch(string provided, string expected)
    {
        byte[] providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        byte[] expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(providedHash, expectedHash);
    }
}
