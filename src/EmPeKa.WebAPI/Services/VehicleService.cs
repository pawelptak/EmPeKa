using EmPeKa.Models;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;
using System.Text;

namespace EmPeKa.Services;

public interface IVehicleService
{
    Task<List<VehiclePosition>> GetVehiclePositionsAsync();
    Task<List<VehiclePosition>> GetVehiclesForLineAsync(string line);
    Task<List<VehiclePosition>> GetVehiclesForLinesAsync(IEnumerable<string> lines, string vehicleType);
}

public class VehicleService : IVehicleService
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<VehicleService> _logger;
    private readonly IGtfsService _gtfsService;
    private readonly SemaphoreSlim _semaphore; // Limit concurrent API calls
    
    private const string CacheKey = "vehicle_positions";
    private const string TramLinesCacheKey = "tram_lines";
    private const string BusLinesCacheKey = "bus_lines";
    private const string ApiUrl = "https://mpk.wroc.pl/bus_position";
    private readonly TimeSpan _cacheDuration = TimeSpan.FromSeconds(30); // Reduced cache time for more up-to-date positions
    private readonly TimeSpan _linesCacheDuration = TimeSpan.FromHours(1); // Cache lines for 1 hour
    private const int MaxConcurrentRequests = 5; // Limit concurrent HTTP requests

    public VehicleService(HttpClient httpClient, IMemoryCache cache, ILogger<VehicleService> logger, IGtfsService gtfsService)
    {
        _httpClient = httpClient;
        _cache = cache;
        _logger = logger;
        _gtfsService = gtfsService;
        _semaphore = new SemaphoreSlim(MaxConcurrentRequests, MaxConcurrentRequests);
    }

    public async Task<List<VehiclePosition>> GetVehiclePositionsAsync()
    {
        if (_cache.TryGetValue(CacheKey, out List<VehiclePosition>? cachedPositions))
        {
            return cachedPositions ?? new List<VehiclePosition>();
        }

        try
        {
            var allPositions = new List<VehiclePosition>();

            // Get tram lines from GTFS data (with caching)
            var tramLines = await GetCachedTramLinesAsync();
            var tramPositions = await GetVehiclesByTypeAsync("tram", tramLines);
            allPositions.AddRange(tramPositions);

            // Get bus lines from GTFS data (with caching)
            var busLines = await GetCachedBusLinesAsync();
            var busPositions = await GetVehiclesByTypeAsync("bus", busLines);
            allPositions.AddRange(busPositions);

            _cache.Set(CacheKey, allPositions, _cacheDuration);
            _logger.LogInformation("Retrieved {Count} vehicle positions ({TramCount} trams, {BusCount} buses). Queried {TramLines} tram lines and {BusLines} bus lines", 
                allPositions.Count, tramPositions.Count, busPositions.Count, tramLines.Count, busLines.Count);
            
            return allPositions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve vehicle positions");
            return new List<VehiclePosition>();
        }
    }

    private async Task<List<string>> GetCachedTramLinesAsync()
    {
        if (_cache.TryGetValue(TramLinesCacheKey, out List<string>? cachedTramLines))
        {
            return cachedTramLines ?? new List<string>();
        }

        var tramLines = await _gtfsService.GetTramLinesAsync();
        _cache.Set(TramLinesCacheKey, tramLines, _linesCacheDuration);
        return tramLines;
    }

    private async Task<List<string>> GetCachedBusLinesAsync()
    {
        if (_cache.TryGetValue(BusLinesCacheKey, out List<string>? cachedBusLines))
        {
            return cachedBusLines ?? new List<string>();
        }

        var busLines = await _gtfsService.GetBusLinesAsync();
        _cache.Set(BusLinesCacheKey, busLines, _linesCacheDuration);
        return busLines;
    }

    private async Task<List<VehiclePosition>> GetVehiclesByTypeAsync(string vehicleType, List<string> lines)
    {
        var allResults = new List<VehiclePosition>();
        
        // Process lines in smaller batches to avoid overwhelming the system
        const int batchSize = 10;
        for (int i = 0; i < lines.Count; i += batchSize)
        {
            var batch = lines.Skip(i).Take(batchSize);
            var tasks = batch.Select(async line => {
                await _semaphore.WaitAsync(); // Limit concurrent requests
                try
                {
                    return await GetVehiclesForLineAndTypeAsync(line, vehicleType);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get {VehicleType} positions for line {Line}", vehicleType, line);
                    return new List<VehiclePosition>();
                }
                finally
                {
                    _semaphore.Release();
                }
            }).ToList();

            var batchResults = await Task.WhenAll(tasks);
            allResults.AddRange(batchResults.SelectMany(x => x));
            
            // Small delay between batches to prevent overwhelming the API
            if (i + batchSize < lines.Count)
            {
                await Task.Delay(100);
            }
        }
        
        return allResults;
    }

    private async Task<List<VehiclePosition>> GetVehiclesForLineAndTypeAsync(string line, string vehicleType)
    {
        try
        {
            // Create form data payload: busList[tram][] 3 or busList[bus][] 248
            var formData = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>($"busList[{vehicleType}][]", line)
            };

            var formContent = new FormUrlEncodedContent(formData);
            var response = await _httpClient.PostAsync(ApiUrl, formContent);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("API request failed for {VehicleType} line {Line}: {StatusCode}", 
                    vehicleType, line, response.StatusCode);
                return new List<VehiclePosition>();
            }

            var jsonContent = await response.Content.ReadAsStringAsync();
            
            if (string.IsNullOrEmpty(jsonContent) || jsonContent == "[]")
            {
                return new List<VehiclePosition>();
            }

            var mpkPositions = JsonSerializer.Deserialize<List<MpkVehiclePosition>>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (mpkPositions == null)
            {
                return new List<VehiclePosition>();
            }

            // Convert MPK positions to our VehiclePosition format with validation
            var positions = mpkPositions
                .Where(mpkPos => IsValidPosition(mpkPos.X, mpkPos.Y)) // Filter out invalid positions
                .Select(mpkPos => new VehiclePosition
                {
                    Id = (int)(mpkPos.K % int.MaxValue), // Use K as ID (truncate if needed)
                    NrBoczny = mpkPos.K, // Use K as vehicle number
                    NrRej = null, // Not available in new API
                    Brygada = null, // Not available in new API
                    NazwaLinii = mpkPos.Name,
                    OstatniaPositionSzerokosc = mpkPos.X, // X is latitude
                    OstatniaPositionDlugosc = mpkPos.Y, // Y is longitude
                    DataAktualizacji = DateTime.Now // Use current time since API doesn't provide timestamp
                }).ToList();

            _logger.LogInformation("Converted {ValidCount} valid positions out of {TotalCount} for {VehicleType} line {Line}", 
                positions.Count, mpkPositions.Count, vehicleType, line);

            return positions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve {VehicleType} positions for line {Line}", vehicleType, line);
            return new List<VehiclePosition>();
        }
    }

    public async Task<List<VehiclePosition>> GetVehiclesForLineAsync(string line)
    {
        var allPositions = await GetVehiclePositionsAsync();
        return allPositions
            .Where(p => string.Equals(p.NazwaLinii, line, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task<List<VehiclePosition>> GetVehiclesForLinesAsync(IEnumerable<string> lines, string vehicleType)
    {
        var tasks = lines.Select(async line => {
            try
            {
                return await GetVehiclesForLineAndTypeAsync(line, vehicleType);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get {VehicleType} positions for line {Line}", vehicleType, line);
                return new List<VehiclePosition>();
            }
        }).ToList();

        var results = await Task.WhenAll(tasks);
        return results.SelectMany(x => x).ToList();
    }
    
    /// <summary>
    /// Validates GPS coordinates to ensure they are reasonable for Wroc³aw area
    /// </summary>
    /// <param name="latitude">Latitude coordinate</param>
    /// <param name="longitude">Longitude coordinate</param>
    /// <returns>True if coordinates appear valid</returns>
    private static bool IsValidPosition(double latitude, double longitude)
    {
        // Check for obviously invalid coordinates
        if (latitude == 0.0 && longitude == 0.0)
            return false; // Common API error response
            
        if (double.IsNaN(latitude) || double.IsNaN(longitude))
            return false; // Invalid numbers
            
        if (double.IsInfinity(latitude) || double.IsInfinity(longitude))
            return false; // Infinity values
        
        // Wroc³aw area bounds (with reasonable buffer)
        // Wroc³aw is roughly at 51.1079° N, 17.0385° E
        const double minLatitude = 50.8;   // South bound
        const double maxLatitude = 51.4;   // North bound  
        const double minLongitude = 16.7;  // West bound
        const double maxLongitude = 17.3;  // East bound
        
        if (latitude < minLatitude || latitude > maxLatitude)
            return false; // Outside Wroc³aw area
            
        if (longitude < minLongitude || longitude > maxLongitude)
            return false; // Outside Wroc³aw area
            
        return true;
    }
}