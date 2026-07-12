namespace ComposeNowPlugins.Transports;

public interface IRuntimeSessionFactory
{
    IRuntimeSession Create(HttpContext context);
}