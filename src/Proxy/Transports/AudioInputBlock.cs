namespace ComposeNowPlugins.Proxy.Transports;

public sealed record AudioInputBlock(
    int Frames,
    int Channels,
    float[] Audio
);
