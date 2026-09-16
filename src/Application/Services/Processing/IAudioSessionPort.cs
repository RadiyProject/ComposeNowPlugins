namespace ComposeNowPlugins.Application.Services.Processing;

public interface IAudioSessionPort
{
    Task StartAsync(CancellationToken cancellationToken);
    Task WaitForPermitAsync(CancellationToken cancellationToken);
    int TakeFrames(ulong sequence, int fallback);
    Task<AudioSessionInput?> TakeInputAsync(
        ulong sequence,
        bool required,
        CancellationToken cancellationToken
    );
    ValueTask WriteOutputAsync(
        ulong sequence,
        ulong timestamp,
        int frames,
        ReadOnlyMemory<float> audio,
        CancellationToken cancellationToken
    );
    void CompleteBlock(ulong sequence);
}
