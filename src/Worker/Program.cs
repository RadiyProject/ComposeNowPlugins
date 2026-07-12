using ComposeNowPlugins.Extensions;
using Worker.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("plugins.catalog.json", optional: false, reloadOnChange: true);

builder.Services.AddPluginCatalog(builder.Configuration);
builder.Services.AddRepositories();
builder.Services.AddRedisCache();
builder.Services.AddVstProcessing();
builder.Services.AddGrpc();

var app = builder.Build();

app.MapGrpcService<PluginWorkerService>();
app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();
