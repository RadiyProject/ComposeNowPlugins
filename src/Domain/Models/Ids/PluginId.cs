using System.Text.Json.Serialization;

namespace ComposeNowPlugins.Domain.Models.Ids;

[JsonConverter(typeof(IdJsonConverter<PluginId, string>))]
public class PluginId : Id<string>
{
    public const int MaxLength = 128;

    public PluginId(string id) : base(id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Plugin id cannot be empty.", nameof(id));
        }

        if (id.Length > MaxLength)
        {
            throw new ArgumentException(
                $"Plugin id cannot exceed {MaxLength} characters.",
                nameof(id)
            );
        }
    }

    public static PluginId New()
    {
        return new PluginId(Guid.CreateVersion7().ToString());
    }
}
