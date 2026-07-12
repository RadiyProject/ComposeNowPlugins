namespace ComposeNowPlugins.Proxy.Transports;

public sealed record HelloOptions(
    int SampleRate,
    int BlockSize,
    int Channels,
    string Mode
);
