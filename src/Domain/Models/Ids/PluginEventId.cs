using System.Text.Json.Serialization;

namespace ComposeNowPlugins.Domain.Models.Ids;

[JsonConverter(typeof(IdJsonConverter<PluginEventId, string>))]
public class PluginEventId : Id<string>
{
    public PluginEventId(string id) : base(id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Plugin id cannot be empty.", nameof(id));
        }
    }

    public static PluginEventId New()
    {
        return new PluginEventId(Guid.CreateVersion7().ToString());
    }
}