var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

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