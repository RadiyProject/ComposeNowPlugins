namespace ComposeNowPlugins.Exceptions;

public sealed class EntityAlreadyExistsException : RepositoryException
{
    public EntityAlreadyExistsException(string message)
        : base(message)
    {
    }
}
