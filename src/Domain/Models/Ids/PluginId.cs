using System.Text.Json.Serialization;

namespace ComposeNowPlugins.Domain.Models.Ids;

[JsonConverter(typeof(IdJsonConverter<PluginId, string>))]
public class PluginId : Id<string>
{
    public PluginId(string id) : base(id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Plugin id cannot be empty.", nameof(id));
        }
    }

    public static PluginId New()
    {
        return new PluginId(Guid.CreateVersion7().ToString());
    }
}