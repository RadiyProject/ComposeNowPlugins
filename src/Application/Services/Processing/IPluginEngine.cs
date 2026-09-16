using ComposeNowPlugins.Domain.Configurations;

namespace ComposeNowPlugins.Application.Services.Processing;

public interface IPluginEngine
{
    int SampleRate { get; }
    int BlockSize { get; }
    int Channels { get; }
    PluginProcessingMode ProcessingMode { get; }

    void NoteOn(int note, float velocity);
    void NoteOff(int note);
    void SetParameterIfChanged(uint id, float value);
    bool SetStateIfChanged(byte[]? state);
    byte[] GetState();
    bool Reconfigure(int sampleRate, int blockSize, int channels, bool offline);
    ReadOnlyMemory<float> Process(int frames);
    ReadOnlyMemory<float> Process(ReadOnlySpan<float> input, int frames);
}
