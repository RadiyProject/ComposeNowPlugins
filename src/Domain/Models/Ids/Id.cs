namespace ComposeNowPlugins.Domain.Models.Ids;

public abstract class Id<T>(T id) : IEquatable<Id<T>>
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

    public bool Equals(Id<T>? other)
    {
        return other is not null &&
            other.GetType() == GetType() &&
            EqualityComparer<T>.Default.Equals(_id, other._id);
    }

    public override bool Equals(object? obj)
    {
        return obj is Id<T> other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(GetType(), _id);
    }

    public static bool operator ==(Id<T>? left, Id<T>? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(Id<T>? left, Id<T>? right)
    {
        return !Equals(left, right);
    }
}
