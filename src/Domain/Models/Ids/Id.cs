namespace ComposeNowPlugins.Domain.Models.Ids;

public abstract class Id<T>(T id)
{
    private readonly T _id = id;

    public T GetValue()
    {
        return _id;
    }

    public override string ToString()
    {
        return _id?.ToString() ?? string.Empty;
    }
}