using EmPeKa.Services;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();

// Add OpenAPI/Swagger
builder.Services.AddOpenApi();

// Add HTTP client
builder.Services.AddHttpClient<GtfsService>();
builder.Services.AddHttpClient<VehicleService>();

// Add memory cache for vehicle positions
builder.Services.AddMemoryCache();

// Add custom services
builder.Services.AddScoped<IGtfsService, GtfsService>();
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

// Initialize GTFS data on startup
using (var scope = app.Services.CreateScope())
{
    var gtfsService = scope.ServiceProvider.GetRequiredService<IGtfsService>();
    await gtfsService.InitializeAsync();
}

app.Run();
