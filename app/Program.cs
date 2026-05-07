using ComposeNowPlugins.Transports;
using ComposeNowPlugins.Transports.WebSockets;
using ComposeNowPlugins.Wrappers;
using StackExchange.Redis;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

builder.Services.AddScoped<VstEngine>();

builder.Services.AddScoped<IRuntimeSessionFactory, RuntimeSessionFactory>();
builder.Services.AddScoped<EchoRuntimeSession>();
builder.Services.AddScoped<AudioRuntimeSession>();

builder.Services.AddScoped<WebSocketTransport>();
builder.Services.AddScoped<IRealtimeTransport>(serviceProvider =>//Decorator (Scrutor)
    new LoggingTransport(
        serviceProvider.GetRequiredService<WebSocketTransport>(),
        serviceProvider.GetRequiredService<ILogger<LoggingTransport>>()
    ));

string? redisPassword = Environment.GetEnvironmentVariable("REDIS_PASSWORD");
builder.Services.AddSingleton<IConnectionMultiplexer>(
        ConnectionMultiplexer.Connect($"redis:6379,password={redisPassword}")
    );

var app = builder.Build();

app.MapGet("/healthcheck", () =>
{
    return "Everything work's fine";
});

app.UseRouting();

var webSocketOptions = new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromMinutes(2)
};

//webSocketOptions.AllowedOrigins.Add("https://client.com");

app.UseWebSockets(webSocketOptions);

app.MapControllers();

app.Run();