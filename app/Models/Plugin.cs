using ComposeNowPlugins.Configurations;
using ComposeNowPlugins.Models.Ids;

namespace ComposeNowPlugins.Models;

public class Plugin(
    PluginId id,
    PluginType type,
    byte[]? state = null,
    Dictionary<uint, float>? parameters = null,
    int sampleRate = 48000,
    int blockSize = 512,
    int channels = 2
) : Model<PluginId>(id)
{
    public PluginType Type { get; private set; } = type;

    /// <summary>
    /// Бинарное состояние VST-плагина.
    /// В JSON byte[] будет сохранён как base64-строка.
    /// </summary>
    public byte[] State { get; private set; } = state ?? [];

    /// <summary>
    /// Последние известные нормализованные параметры плагина.
    /// Ключ — ParamID, значение — normalized value 0..1.
    /// </summary>
    public Dictionary<uint, float> Parameters { get; private set; } = parameters ?? [];

    public int SampleRate { get; private set; } = sampleRate;

    public int BlockSize { get; private set; } = blockSize;

    public int Channels { get; private set; } = channels;

    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public Plugin(
        PluginType type,
        byte[]? state = null, 
        Dictionary<uint, float>? parameters = null,
        int sampleRate = 48000, 
        int blockSize = 512, 
        int channels = 2
    ) 
        : this(
            PluginId.New(),
            type,
            state,
            parameters,
            sampleRate,
            blockSize,
            channels
        )
    {
    }

    public void SetState(byte[] state)
    {
        State = state;
        Touch();
    }

    public void SetParameter(uint id, float value)
    {
        Parameters[id] = value;
        Touch();
    }

    public void SetParameters(Dictionary<uint, float> parameters)
    {
        Parameters = parameters;
        Touch();
    }

    public void SetAudioConfiguration(
        int sampleRate,
        int blockSize,
        int channels
    )
    {
        SampleRate = sampleRate;
        BlockSize = blockSize;
        Channels = channels;
        Touch();
    }

    private void Touch()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}