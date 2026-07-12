namespace ComposeNowPlugins.Exceptions;

public class RepositoryException : AppException
{
    public RepositoryException(string message)
        : base(message)
    {
    }

    public RepositoryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
