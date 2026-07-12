namespace ComposeNowPlugins.Domain.Models;

public abstract class Model<TId>(TId id)
{
    public TId Id { get; } = id;
}