using System.Globalization;
using ComposeNowPlugins.Application.Repositories.Plugins;
using ComposeNowPlugins.Domain.Models;
using ComposeNowPlugins.Domain.Models.Ids;

namespace ComposeNowPlugins.Proxy.Transports;

public sealed class PluginEventCommandHandler(
    PluginId pluginId,
    IPluginEventRepository pluginEventRepository
)
{
    private readonly PluginId _pluginId = pluginId;
    private readonly IPluginEventRepository _pluginEventRepository = pluginEventRepository;

    public async Task<bool> TryHandleAsync(string text)
    {
        if (await TryHandleNoteOnAsync(text))
        {
            return true;
        }

        if (await TryHandleNoteOffAsync(text))
        {
            return true;
        }

        if (await TryHandleParamAsync(text))
        {
            return true;
        }

        return await TryHandlePanicAsync(text);
    }

    private async Task<bool> TryHandleNoteOnAsync(string text)
    {
        if (!text.StartsWith("note on ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] parts = text.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries
        );

        if (parts.Length < 3)
        {
            return true;
        }

        if (!int.TryParse(parts[2], out int note))
        {
            return true;
        }

        float velocity = 1f;

        if (parts.Length > 3)
        {
            float.TryParse(
                parts[3],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out velocity
            );
        }

        await _pluginEventRepository.AddControlEventAsync(
            _pluginId,
            PluginEvent.NoteOn(
                _pluginId,
                note,
                velocity
            )
        );

        return true;
    }

    private async Task<bool> TryHandleNoteOffAsync(string text)
    {
        if (!text.StartsWith("note off ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] parts = text.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries
        );

        if (parts.Length < 3)
        {
            return true;
        }

        if (!int.TryParse(parts[2], out int note))
        {
            return true;
        }

        await _pluginEventRepository.AddControlEventAsync(
            _pluginId,
            PluginEvent.NoteOff(
                _pluginId,
                note
            )
        );

        return true;
    }

    private async Task<bool> TryHandleParamAsync(string text)
    {
        if (!text.StartsWith("param ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] parts = text.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries
        );

        if (parts.Length < 3)
        {
            return true;
        }

        if (!uint.TryParse(parts[1], out uint parameterId))
        {
            return true;
        }

        if (!float.TryParse(
            parts[2],
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out float value
        ))
        {
            return true;
        }

        await _pluginEventRepository.AddControlEventAsync(
            _pluginId,
            PluginEvent.Param(
                _pluginId,
                parameterId,
                value
            )
        );

        return true;
    }

    private async Task<bool> TryHandlePanicAsync(string text)
    {
        if (!text.Equals("panic", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        await _pluginEventRepository.AddControlEventAsync(
            _pluginId,
            PluginEvent.Panic(_pluginId)
        );

        return true;
    }
}
