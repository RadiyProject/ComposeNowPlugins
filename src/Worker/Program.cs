using ComposeNowPlugins.Worker.Extensions;
using ComposeNowPlugins.Worker.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("plugins.catalog.json", optional: false, reloadOnChange: true);

builder.Services.AddWorkerApplication(builder.Configuration);

var app = builder.Build();

app.MapGrpcService<PluginWorkerService>();
app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();
