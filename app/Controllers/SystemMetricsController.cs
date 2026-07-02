using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ComposeNowPlugins.Controllers;

[ApiController]
[Route("internal/system/metrics")]
public sealed class SystemMetricsController(IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        var expected = configuration["Infrastructure:MetricsKey"]
            ?? Environment.GetEnvironmentVariable("COMPOSE_NOW_METRICS_KEY")
            ?? "compose-now-development-metrics";
        if (Request.Headers["X-ComposeNow-Metrics-Key"] != expected)
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
            details = $"{process.Threads.Count} потоков · {Environment.ProcessorCount} CPU"
        });
    }
}
