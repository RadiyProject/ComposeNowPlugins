namespace ComposeNowPlugins.Application.Exceptions;

public sealed class EntityAlreadyExistsException : AppException
{
    public EntityAlreadyExistsException(string message)
        : base(message)
    {
    }
}
