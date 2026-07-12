namespace ComposeNowPlugins.Proxy.Transports;

public interface IRuntimeSessionFactory
{
    IRuntimeSession Create(HttpContext context);
}