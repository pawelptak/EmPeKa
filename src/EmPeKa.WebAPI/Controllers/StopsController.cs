using Microsoft.AspNetCore.Mvc;
using EmPeKa.Models;
using EmPeKa.Services;

namespace EmPeKa.Controllers;

[ApiController]
[Route("[controller]")]
[Produces("application/json")]
public class StopsController : ControllerBase
{
    private readonly IGtfsService _gtfsService;
    private readonly ITransitService _transitService;
    private readonly ILogger<StopsController> _logger;

    public StopsController(IGtfsService gtfsService, ITransitService transitService, ILogger<StopsController> logger)
    {
        _gtfsService = gtfsService;
        _transitService = transitService;
        _logger = logger;
    }

    /// <summary>
    /// Gets all available stops or filters by stop ID
    /// </summary>
    /// <param name="stopId">Optional stop ID to filter by</param>
    /// <returns>List of stops with their available lines</returns>
    [HttpGet]
    [ProducesResponseType(typeof(StopsResponse), 200)]
    public async Task<ActionResult<StopsResponse>> GetStops([FromQuery] string? stopId = null)
    {
        try
        {
            _logger.LogInformation("Getting stops with filter: {StopId}", stopId ?? "none");
            
            var stops = await _gtfsService.GetStopsAsync(stopId);
            
            var response = new StopsResponse
            {
                Stops = stops,
                Total = stops.Count
            };
            
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting stops");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Gets upcoming arrivals for a specific stop
    /// </summary>
    /// <param name="stopCode">The stop code to get arrivals for</param>
    /// <returns>List of upcoming arrivals</returns>
    [HttpGet("{stopCode}/arrivals")]
    [ProducesResponseType(typeof(ArrivalsResponse), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<ArrivalsResponse>> GetArrivals(string stopCode, [FromQuery] int count = 3)
    {
        try
        {
            _logger.LogInformation("Getting arrivals for stopCode: {StopCode} (count={Count})", stopCode, count);
            var arrivals = await _transitService.GetArrivalsAsync(stopCode, count);
            if (arrivals == null)
            {
                return NotFound(new { error = $"Stop code '{stopCode}' not found" });
            }
            return Ok(arrivals);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting arrivals for stopCode {StopCode}", stopCode);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}