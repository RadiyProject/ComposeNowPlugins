namespace ComposeNowPlugins.Transports;

public sealed class EchoRuntimeSession(ILogger<EchoRuntimeSession> log, IHostEnvironment env) : IRuntimeSession
{
    private readonly ILogger<EchoRuntimeSession> _log = log;
    private readonly bool _dev = env.IsDevelopment();

    public async Task RunAsync(IRuntimeChannel channel, CancellationToken cancellationToken)
    {
        await foreach (var msg in channel.ReadAllAsync(cancellationToken))
        {
            if (_dev && msg.ContentType == "text/plain")
            {
                var span = msg.Payload.Span;
                var previewLen = Math.Min(span.Length, 200);
                var preview = System.Text.Encoding.UTF8.GetString(span[..previewLen]);
                if (span.Length > previewLen) preview += "…";
                _log.LogInformation("In {Transport} len={Len} ct={CT} text='{Preview}'",
                    channel.Transport, msg.Payload.Length, msg.ContentType, preview);
            }
            else
                _log.LogInformation("In {Transport} len={Len} ct={CT}",
                    channel.Transport, msg.Payload.Length, msg.ContentType);

            await channel.SendAsync(msg.Payload, msg.ContentType, endOfMessage: true, cancellationToken);
        }
    }
}
