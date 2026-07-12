using System.Text.Json.Serialization;
using ComposeNowPlugins.Domain.Configurations;
using ComposeNowPlugins.Domain.Models.Ids;

namespace ComposeNowPlugins.Domain.Models;

[method: JsonConstructor]
public class Plugin(
    PluginId id,
    PluginDescriptor descriptor,
    byte[]? state = null,
    Dictionary<uint, float>? parameters = null,
    HashSet<int>? activeNotes = null,
    bool waitingForReleaseSilence = false,
    int consecutiveSilentBlocks = 0,
    PluginProcessingMode processingMode = PluginProcessingMode.Realtime,
    int sampleRate = 44100,
    int blockSize = 512,
    int channels = 2
) : Model<PluginId>(id)
{
    public PluginDescriptor Descriptor { get; private set; } = descriptor;

    /// <summary>
    /// Бинарное состояние VST-плагина.
    /// В JSON byte[] будет сохранён как base64-строка.
    /// </summary>
    public byte[] State { get; private set; } = state ?? [];

    public PluginProcessingMode ProcessingMode { get; private set; } = processingMode;

    /// <summary>
    /// Последние известные нормализованные параметры плагина.
    /// Ключ — ParamID, значение — normalized value 0..1.
    /// </summary>
    public Dictionary<uint, float> Parameters { get; private set; } = parameters ?? [];
    public HashSet<int> ActiveNotes { get; private set; } = activeNotes ?? [];

    public bool WaitingForReleaseSilence { get; private set; } = waitingForReleaseSilence;
    public int ConsecutiveSilentBlocks { get; private set; } = consecutiveSilentBlocks;

    public int SampleRate { get; private set; } = sampleRate;

    public int BlockSize { get; private set; } = blockSize;

    public int Channels { get; private set; } = channels;

    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public bool HasActiveAudio()
    {
        return ActiveNotes.Count > 0 || WaitingForReleaseSilence;
    }

    public void MarkNoteOn(int note)
    {
        ActiveNotes.Add(note);
        WaitingForReleaseSilence = false;
        ConsecutiveSilentBlocks = 0;
        Touch();
    }

    public void MarkNoteOff(int note)
    {
        ActiveNotes.Remove(note);

        if (ActiveNotes.Count == 0)
        {
            WaitingForReleaseSilence = true;
            ConsecutiveSilentBlocks = 0;
        }

        Touch();
    }

    public void MarkAudioActivity(bool isSilent, int requiredSilentBlocks = 8)
    {
        if (ActiveNotes.Count > 0)
        {
            WaitingForReleaseSilence = false;
            ConsecutiveSilentBlocks = 0;
            Touch();
            return;
        }

        if (!WaitingForReleaseSilence)
        {
            return;
        }

        if (isSilent)
        {
            ConsecutiveSilentBlocks++;

            if (ConsecutiveSilentBlocks >= requiredSilentBlocks)
            {
                WaitingForReleaseSilence = false;
                ConsecutiveSilentBlocks = 0;
            }
        }
        else
        {
            ConsecutiveSilentBlocks = 0;
        }

        Touch();
    }

    public void Panic()
    {
        ActiveNotes.Clear();
        WaitingForReleaseSilence = false;
        ConsecutiveSilentBlocks = 0;
        Touch();
    }

    public void SetState(byte[] state)
    {
        State = state;
        Touch();
    }

    public void SetProcessingMode(PluginProcessingMode mode)
    {
        ProcessingMode = mode;
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