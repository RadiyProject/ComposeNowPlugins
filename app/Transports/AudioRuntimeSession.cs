namespace ComposeNowPlugins.Transports;

public sealed class AudioRuntimeSession(ILogger<AudioRuntimeSession> log) : IRuntimeSession
{
    private readonly ILogger<AudioRuntimeSession> _log = log;

    public async Task RunAsync(IRuntimeChannel ch, CancellationToken ct)
    {
        await foreach (var msg in ch.ReadAllAsync(ct))
        {
            if (msg.ContentType is not null && msg.ContentType != "application/octet-stream")
                continue;

            var ok = TryParseAud0(msg.Payload.Span, out var meta, out var payload);
            if (!ok) { _log.LogWarning("AUD0 invalid frame"); continue; }

            // TODO: обработка payload (gain/фильтры и т.п.)
            await ch.SendAsync(msg.Payload, "application/octet-stream", true, ct);
        }
    }

    private static bool TryParseAud0(ReadOnlySpan<byte> buf, out object meta, out ReadOnlySpan<byte> payload)
    {
        meta = default!;
        payload = default;
        if (buf.Length < 4) return false;
        if (!(buf[0] == 'A' && buf[1] == 'U' && buf[2] == 'D' && buf[3] == '0')) return false;
        // распарсить заголовок (как делали ранее), установить payload
        // ...
        return true;
    }
}
