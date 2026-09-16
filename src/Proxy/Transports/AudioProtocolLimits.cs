using ComposeNowPlugins.Application.Services.Processing;

namespace ComposeNowPlugins.Proxy.Transports;

public static class AudioProtocolLimits
{
    public const int MaxWebSocketMessageBytes = 16 * 1024 * 1024;
    public const int MaxTextMessageBytes = 4 * 1024;
    public const int MaxSampleRate = AudioProcessingLimits.MaxSampleRate;
    public const int MaxFramesPerBlock = AudioProcessingLimits.MaxFramesPerBlock;
    public const int MaxChannels = AudioProcessingLimits.MaxChannels;
    public const int MaxEventsPerBlock = AudioProcessingLimits.MaxEventsPerBlock;
    public const int MaxOutstandingCredits = 4_096;
    public const int MaxCompressedBytes = 8 * 1024 * 1024;
    public const int MaxUncompressedBytes = 16 * 1024 * 1024;
    public const int MaxBufferedBlocks = 256;
    public const ulong MaxFutureSequenceDistance = 512;
}
