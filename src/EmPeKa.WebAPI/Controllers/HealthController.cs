using Microsoft.AspNetCore.Mvc;
using EmPeKa.WebAPI.Services;
using EmPeKa.WebAPI.Interfaces;

namespace EmPeKa.Controllers;

[ApiController]
[Route("[controller]")]
[Produces("application/json")]
public class HealthController : ControllerBase
{
    private readonly IGtfsService _gtfsService;
    private readonly IVehicleService _vehicleService;
    private readonly ILogger<HealthController> _logger;

    public HealthController(IGtfsService gtfsService, IVehicleService vehicleService, ILogger<HealthController> logger)
    {
        _gtfsService = gtfsService;
        _vehicleService = vehicleService;
        _logger = logger;
    }

    /// <summary>
    /// Health check endpoint
    /// </summary>
    /// <returns>API status and data availability</returns>
    [HttpGet]
    [ProducesResponseType(200)]
    public async Task<ActionResult<object>> GetHealth()
    {
        try
        {
            // Check GTFS data
            var stops = await _gtfsService.GetStopsAsync();
            var gtfsHealthy = stops.Any();
            
            // Check real-time data
            var vehicles = await _vehicleService.GetVehiclePositionsAsync();
            var realtimeHealthy = vehicles.Any();
            
            var health = new
            {
                status = "healthy",
                timestamp = DateTime.UtcNow,
                gtfs = new
                {
                    healthy = gtfsHealthy,
                    stopsCount = stops.Count
                },
                realtime = new
                {
                    healthy = realtimeHealthy,
                    vehiclesCount = vehicles.Count,
                    lastUpdate = vehicles.Any() ? vehicles.Max(v => v.LastUpdated) : (DateTime?)null
                }
            };
            
            return Ok(health);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            return StatusCode(500, new { status = "unhealthy", error = ex.Message });
        }
    }
}