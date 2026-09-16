using System.Text.Json.Serialization;
using ComposeNowPlugins.Domain.Configurations;
using ComposeNowPlugins.Domain.Models.Ids;

namespace ComposeNowPlugins.Domain.Models;

public sealed class Plugin : Model<PluginId>
{
    public const int MaxTrackedParameters = 16_384;

    private byte[] _state = [];
    private PluginProcessingMode _processingMode = PluginProcessingMode.Realtime;
    private Dictionary<uint, float> _parameters = [];
    private int _sampleRate = 44100;
    private int _blockSize = 512;
    private int _channels = 2;

    public required PluginDescriptor Descriptor { get; init; }

    /// <summary>
    /// Binary VST plug-in state.
    /// In JSON, byte[] is serialized as a base64 string.
    /// </summary>
    public byte[] State
    {
        get => _state;
        set
        {
            _state = value ?? throw new ArgumentNullException(nameof(value));
            Touch();
        }
    }

    public PluginProcessingMode ProcessingMode
    {
        get => _processingMode;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _processingMode = value;
            Touch();
        }
    }

    /// <summary>
    /// Last known normalized plug-in parameters.
    /// Key: ParamID; value: normalized value in 0..1.
    /// </summary>
    public IReadOnlyDictionary<uint, float> Parameters
    {
        get => _parameters;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Count > MaxTrackedParameters)
            {
                throw new ArgumentException(
                    $"Plugin cannot track more than {MaxTrackedParameters} parameters.",
                    nameof(value)
                );
            }

            foreach ((uint parameterId, float parameterValue) in value)
            {
                ValidateParameter(parameterId, parameterValue);
            }

            _parameters = new Dictionary<uint, float>(value);
            Touch();
        }
    }
    [JsonInclude]
    public HashSet<int> ActiveNotes { get; private set; } = [];

    [JsonInclude]
    public bool WaitingForReleaseSilence { get; private set; }

    [JsonInclude]
    public int ConsecutiveSilentBlocks { get; private set; }

    public int SampleRate
    {
        get => _sampleRate;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _sampleRate = value;
            Touch();
        }
    }

    public int BlockSize
    {
        get => _blockSize;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _blockSize = value;
            Touch();
        }
    }

    public int Channels
    {
        get => _channels;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _channels = value;
            Touch();
        }
    }

    [JsonInclude]
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public bool HasActiveAudio()
    {
        return ActiveNotes.Count > 0 || WaitingForReleaseSilence;
    }

    public void MarkNoteOn(int note)
    {
        ValidateNote(note);
        ActiveNotes.Add(note);
        WaitingForReleaseSilence = false;
        ConsecutiveSilentBlocks = 0;
        Touch();
    }

    public void MarkNoteOff(int note)
    {
        ValidateNote(note);
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
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requiredSilentBlocks);
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

    public void ChangeParameter(uint id, float value)
    {
        ValidateParameter(id, value);
        if (!_parameters.ContainsKey(id) && _parameters.Count >= MaxTrackedParameters)
        {
            throw new InvalidOperationException(
                $"Plugin cannot track more than {MaxTrackedParameters} parameters."
            );
        }

        _parameters[id] = value;
        Touch();
    }

    private void Touch()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void ValidateNote(int note)
    {
        if (note is < 0 or > 127)
        {
            throw new ArgumentOutOfRangeException(nameof(note), note, "MIDI note must be between 0 and 127.");
        }
    }

    private static void ValidateParameter(uint id, float value)
    {
        if (!float.IsFinite(value) || value is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"Plugin parameter {id} must be a finite normalized value between 0 and 1."
            );
        }
    }
}
