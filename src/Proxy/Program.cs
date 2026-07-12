using ComposeNowPlugins.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("plugins.catalog.json", optional: false, reloadOnChange: true);
builder.Services.AddApplicationServices(builder.Configuration);

var app = builder.Build();

app.UseApplicationPipeline();

app.Run();