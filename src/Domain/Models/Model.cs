namespace ComposeNowPlugins.Domain.Models;

public abstract class Model<TId>
{
    public required TId Id { get; init; }
}
