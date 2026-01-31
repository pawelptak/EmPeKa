using CsvHelper;
using EmPeKa.Models;
using System.Globalization;
using System.IO.Compression;
using GtfsRoute = EmPeKa.Models.Route;

namespace EmPeKa.Services;

public interface IGtfsService
{
    Task InitializeAsync();
    Task<List<StopInfo>> GetStopsAsync(string? stopId = null);
    Task<List<StopTime>> GetStopTimesForStopAsync(string stopId);
    Task<GtfsRoute?> GetRouteAsync(string routeId);
    Task<Trip?> GetTripAsync(string tripId);
    Task<List<string>> GetAllLinesAsync();
    Task<List<string>> GetTramLinesAsync();
    Task<List<string>> GetBusLinesAsync();
}

public class GtfsService : IGtfsService
{
    private readonly ILogger<GtfsService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _gtfsDataPath;
    
    private List<Stop> _stops = new();
    private List<GtfsRoute> _routes = new();
    private List<Trip> _trips = new();
    private List<StopTime> _stopTimes = new();
    
    // Pre-computed mapping for performance
    private Dictionary<string, List<string>> _stopToLinesMap = new();
    
    private DateTime _lastUpdate = DateTime.MinValue;
    private readonly TimeSpan _updateInterval = TimeSpan.FromHours(24); // Update daily

    public GtfsService(ILogger<GtfsService> logger, HttpClient httpClient, IConfiguration configuration)
    {
        _logger = logger;
        _httpClient = httpClient;
        _gtfsDataPath = configuration["GtfsDataPath"] ?? Path.Combine(Path.GetTempPath(), "gtfs_data");
        
        Directory.CreateDirectory(_gtfsDataPath);
    }

    public async Task InitializeAsync()
    {
        if (DateTime.Now - _lastUpdate < _updateInterval && _stops.Any())
        {
            _logger.LogInformation("GTFS data is up to date, skipping download");
            return;
        }

        try
        {
            await DownloadAndExtractGtfsData();
            await LoadGtfsData();
            _lastUpdate = DateTime.Now;
            _logger.LogInformation("GTFS data loaded successfully. Stops: {StopsCount}, Routes: {RoutesCount}, Trips: {TripsCount}, StopTimes: {StopTimesCount}", 
                _stops.Count, _routes.Count, _trips.Count, _stopTimes.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize GTFS data");
            throw;
        }
    }

    private async Task DownloadAndExtractGtfsData()
    {
        const string gtfsUrl = "https://www.wroclaw.pl/open-data/87b09b32-f076-4475-8ec9-6020ed1f9ac0/OtwartyWroclaw_rozklad_jazdy_GTFS_17012026.zip";
        
        _logger.LogInformation("Downloading GTFS data from {Url}", gtfsUrl);
        
        var zipPath = Path.Combine(_gtfsDataPath, "gtfs.zip");
        var response = await _httpClient.GetAsync(gtfsUrl);
        response.EnsureSuccessStatusCode();
        
        await using (var fileStream = File.Create(zipPath))
        {
            await response.Content.CopyToAsync(fileStream);
        }
        
        _logger.LogInformation("Extracting GTFS data");
        
        // Clear existing data
        var extractPath = Path.Combine(_gtfsDataPath, "extracted");
        if (Directory.Exists(extractPath))
        {
            Directory.Delete(extractPath, true);
        }
        
        // Wait a moment to ensure file is released
        await Task.Delay(100);
        
        ZipFile.ExtractToDirectory(zipPath, extractPath);
        
        // Delete zip file after extraction
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }
    }

    private async Task LoadGtfsData()
    {
        var extractPath = Path.Combine(_gtfsDataPath, "extracted");
        
        _stops = await LoadCsvFile<Stop>(Path.Combine(extractPath, "stops.txt"));
        _routes = await LoadCsvFile<GtfsRoute>(Path.Combine(extractPath, "routes.txt"));
        _trips = await LoadCsvFile<Trip>(Path.Combine(extractPath, "trips.txt"));
        _stopTimes = await LoadCsvFile<StopTime>(Path.Combine(extractPath, "stop_times.txt"));
        
        // Pre-compute stop to lines mapping for performance
        _stopToLinesMap = BuildStopToLinesMap();
    }

    private Dictionary<string, List<string>> BuildStopToLinesMap()
    {
        _logger.LogInformation("Building stop to lines mapping...");
        
        // Create lookups for performance
        var tripToRoute = _trips.ToDictionary(t => t.TripId, t => t.RouteId);
        var routeToShortName = _routes
            .Where(r => !string.IsNullOrEmpty(r.RouteShortName))
            .ToDictionary(r => r.RouteId, r => r.RouteShortName);
        
        // Group stop times by stop ID and collect unique lines
        var stopToLines = _stopTimes
            .Where(st => tripToRoute.ContainsKey(st.TripId))
            .GroupBy(st => st.StopId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(st => tripToRoute[st.TripId])
                    .Where(routeId => routeToShortName.ContainsKey(routeId))
                    .Select(routeId => routeToShortName[routeId])
                    .Distinct()
                    .OrderBy(name => name)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase
            );
        
        _logger.LogInformation("Stop to lines mapping completed. Mapped {Count} stops", stopToLines.Count);
        return stopToLines;
    }

    private async Task<List<T>> LoadCsvFile<T>(string filePath)
    {
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("CSV file not found: {FilePath}", filePath);
            return new List<T>();
        }

        using var reader = new StringReader(await File.ReadAllTextAsync(filePath));
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        return csv.GetRecords<T>().ToList();
    }

    public async Task<List<StopInfo>> GetStopsAsync(string? stopId = null)
    {
        await InitializeAsync();
        
        var filteredStops = string.IsNullOrEmpty(stopId) 
            ? _stops 
            : _stops.Where(s => s.StopId.Equals(stopId, StringComparison.OrdinalIgnoreCase)).ToList();

        // Fast mapping using pre-computed dictionary
        var result = filteredStops.Select(stop => new StopInfo
        {
            StopId = stop.StopId,
            StopName = stop.StopName,
            Latitude = stop.StopLat,
            Longitude = stop.StopLon,
            Lines = _stopToLinesMap.GetValueOrDefault(stop.StopId, new List<string>())
        }).ToList();

        return result;
    }

    public async Task<List<StopTime>> GetStopTimesForStopAsync(string stopId)
    {
        await InitializeAsync();
        return _stopTimes.Where(st => st.StopId.Equals(stopId, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public async Task<GtfsRoute?> GetRouteAsync(string routeId)
    {
        await InitializeAsync();
        return _routes.FirstOrDefault(r => r.RouteId.Equals(routeId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<Trip?> GetTripAsync(string tripId)
    {
        await InitializeAsync();
        return _trips.FirstOrDefault(t => t.TripId.Equals(tripId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<List<string>> GetAllLinesAsync()
    {
        await InitializeAsync();
        return _routes
            .Where(r => !string.IsNullOrEmpty(r.RouteShortName))
            .Select(r => r.RouteShortName)
            .Distinct()
            .OrderBy(name => name)
            .ToList();
    }

    public async Task<List<string>> GetTramLinesAsync()
    {
        await InitializeAsync();
        return _routes
            .Where(r => !string.IsNullOrEmpty(r.RouteShortName) && r.RouteType == 0) // 0 = Tram
            .Select(r => r.RouteShortName)
            .Distinct()
            .OrderBy(name => name)
            .ToList();
    }

    public async Task<List<string>> GetBusLinesAsync()
    {
        await InitializeAsync();
        return _routes
            .Where(r => !string.IsNullOrEmpty(r.RouteShortName) && r.RouteType == 3) // 3 = Bus
            .Select(r => r.RouteShortName)
            .Distinct()
            .OrderBy(name => name)
            .ToList();
    }

}