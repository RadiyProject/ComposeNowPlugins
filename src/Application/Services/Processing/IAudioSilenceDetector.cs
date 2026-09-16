namespace ComposeNowPlugins.Application.Services.Processing;

public interface IAudioSilenceDetector
{
    public bool IsSilent(
        ReadOnlyMemory<float> audio,
        float threshold
    );
}
