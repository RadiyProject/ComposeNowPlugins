namespace ComposeNowPlugins.Application.Services.Processing;

public sealed record AudioSessionInput(
    int Frames,
    ReadOnlyMemory<float> Audio
);
