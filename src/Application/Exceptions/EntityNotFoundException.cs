namespace ComposeNowPlugins.Exceptions;

public sealed class EntityNotFoundException : RepositoryException
{
    public EntityNotFoundException(string message)
        : base(message)
    {
    }
}
