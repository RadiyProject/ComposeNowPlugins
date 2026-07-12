using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ComposeNowPlugins.Domain.Models.Ids;

public class IdJsonConverter<TId, TValue> : JsonConverter<TId>
    where TId : Id<TValue>
{
    public override TId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        TValue? value = JsonSerializer.Deserialize<TValue>(ref reader, options) 
            ?? throw new JsonException($"{typeof(TId).Name} cannot be null.");
        
        return CreateId(value);
    }

    public override void Write(Utf8JsonWriter writer, TId value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value.GetValue(), options);
    }

    private static TId CreateId(TValue value)
    {
        ConstructorInfo constructor = typeof(TId).GetConstructor(
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: [typeof(TValue)],
                modifiers: null
            ) 
            ?? throw new JsonException($"{typeof(TId).Name} must have a public constructor with one parameter of type {typeof(TValue).Name}.");

        try
        {
            return (TId)constructor.Invoke([value]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw new JsonException(
                $"Cannot create {typeof(TId).Name}: {ex.InnerException.Message}",
                ex.InnerException
            );
        }
    }
}