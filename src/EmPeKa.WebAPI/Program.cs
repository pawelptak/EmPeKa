using EmPeKa.WebAPI.Services;
using EmPeKa.WebAPI.Interfaces;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();

// Add OpenAPI/Swagger
builder.Services.AddOpenApi();

// Add HTTP client with timeouts and connection limits
builder.Services.AddHttpClient<GtfsService>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(5);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler()
{
    MaxConnectionsPerServer = 2
});

builder.Services.AddHttpClient<VehicleService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler()
{
    MaxConnectionsPerServer = 10 // Limit concurrent connections
});

// Add memory cache with size limit for vehicle positions
builder.Services.AddMemoryCache(options =>
{
    options.SizeLimit = 50; // Limit cache size
    options.CompactionPercentage = 0.2; // Remove 20% when limit reached
});

// Add custom services
builder.Services.AddSingleton<IGtfsService, GtfsService>();
builder.Services.AddScoped<IVehicleService, VehicleService>();
builder.Services.AddScoped<ITransitService, TransitService>();

// Add logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(); // Add Scalar UI
}

// Add CORS for development
if (app.Environment.IsDevelopment())
{
    app.UseCors(builder => builder
        .AllowAnyOrigin()
        .AllowAnyMethod()
        .AllowAnyHeader());
}

// Don't redirect to HTTPS in production (for Docker/Linux deployment)
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();
app.MapControllers();

// Initialize GTFS data asynchronously in background to avoid blocking startup
_ = Task.Run(async () =>
{
    try
    {
        using var scope = app.Services.CreateScope();
        var gtfsService = scope.ServiceProvider.GetRequiredService<IGtfsService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        logger.LogInformation("Starting background GTFS initialization...");

        await gtfsService.InitializeAsync();

        logger.LogInformation("Background GTFS initialization completed");
    }
    catch (Exception ex)
    {
        var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<Program>();

        logger.LogError(ex, "Background GTFS initialization failed");
    }
});

app.Run();
