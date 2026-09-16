namespace ComposeNowPlugins.Application.Exceptions;

public sealed class EntityNotFoundException : AppException
{
    public EntityNotFoundException(string message)
        : base(message)
    {
    }
}
