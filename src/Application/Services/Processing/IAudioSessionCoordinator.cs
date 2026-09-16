namespace ComposeNowPlugins.Application.Services.Processing;

public interface IAudioSessionCoordinator
{
    Task RunAsync(
        AudioSessionConfiguration configuration,
        IAudioSessionPort port,
        CancellationToken cancellationToken
    );
}
