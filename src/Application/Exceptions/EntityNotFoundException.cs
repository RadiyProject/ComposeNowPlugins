namespace ComposeNowPlugins.Application.Exceptions;

public sealed class EntityNotFoundException : RepositoryException
{
    public EntityNotFoundException(string message)
        : base(message)
    {
    }
}
