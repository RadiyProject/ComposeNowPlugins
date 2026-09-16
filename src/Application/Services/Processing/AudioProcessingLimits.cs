namespace ComposeNowPlugins.Application.Services.Processing;

public static class AudioProcessingLimits
{
    public const int MaxSampleRate = 768_000;
    public const int MaxFramesPerBlock = 16_384;
    public const int MaxChannels = 32;
    public const int MaxEventsPerBlock = 4_096;
    public const int MaxSamplesPerBlock = MaxFramesPerBlock * MaxChannels;
}
