using ComposeNowPlugins.Domain.Models.Ids;

namespace ComposeNowPlugins.Application.Services.Processing;

public sealed record AudioSessionConfiguration(
    PluginId PluginId,
    ulong Epoch,
    int SampleRate,
    int BlockSize,
    int Channels,
    bool Offline,
    bool RequiresInputAudio
);
