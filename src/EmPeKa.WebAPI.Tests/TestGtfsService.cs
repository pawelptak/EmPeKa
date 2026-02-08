using CsvHelper;
using EmPeKa.WebAPI.Models;
using EmPeKa.WebAPI.Services;
using EmPeKa.WebAPI.Interfaces;
using Microsoft.Extensions.Logging;
using System.Globalization;
using Calendar = EmPeKa.WebAPI.Models.Calendar;

namespace EmPeKa.WebAPI.Tests;

/// <summary>
/// Test-specific implementation of GtfsService that uses local test data without downloading
/// </summary>
public class TestGtfsService : IGtfsService
{
    private readonly ILogger<GtfsService> _logger;
    private readonly string _gtfsDataPath;

    private List<Stop> _stops = [];
    private List<Route> _routes = [];
    private List<Trip> _trips = [];
    private List<StopTime> _stopTimes = [];
    private List<Calendar> _calendar = [];

    // Pre-computed mapping for performance
    private Dictionary<string, List<string>> _stopToLinesMap = [];

    private bool _isInitialized = false;

    public TestGtfsService(ILogger<GtfsService> logger, string testDataPath)
    {
        _logger = logger;
        _gtfsDataPath = testDataPath;
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        try
        {
            await LoadGtfsData();
            _isInitialized = true;
            _logger.LogInformation("Test GTFS data loaded successfully. Stops: {StopsCount}, Routes: {RoutesCount}, Trips: {TripsCount}, StopTimes: {StopTimesCount}",
                _stops.Count, _routes.Count, _trips.Count, _stopTimes.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load test GTFS data");
            throw;
        }
    }

    private async Task LoadGtfsData()
    {
        var extractPath = Path.Combine(_gtfsDataPath, "extracted");

        _stops = await LoadCsvFile<Stop>(Path.Combine(extractPath, "stops.txt"));
        _routes = await LoadCsvFile<Route>(Path.Combine(extractPath, "routes.txt"));
        _trips = await LoadCsvFile<Trip>(Path.Combine(extractPath, "trips.txt"));
        _stopTimes = await LoadCsvFile<StopTime>(Path.Combine(extractPath, "stop_times.txt"));
        _calendar = await LoadCsvFile<Calendar>(Path.Combine(extractPath, "calendar.txt"));

        // Pre-compute stop to lines mapping for performance
        _stopToLinesMap = BuildStopToLinesMap();
    }

    private Dictionary<string, List<string>> BuildStopToLinesMap()
    {
        // Create lookups for performance
        var tripToRoute = _trips.ToDictionary(t => t.TripId, t => t.RouteId);
        var routeToShortName = _routes
            .Where(r => !string.IsNullOrEmpty(r.RouteShortName))
            .ToDictionary(r => r.RouteId, r => r.RouteShortName);

        // Group stop times by stop ID and collect unique lines
        var stopToLines = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in _stopTimes.Where(st => tripToRoute.ContainsKey(st.TripId)).GroupBy(st => st.StopId, StringComparer.OrdinalIgnoreCase))
        {
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var st in group)
            {
                if (tripToRoute.TryGetValue(st.TripId, out var routeId) && routeToShortName.TryGetValue(routeId, out var shortName))
                {
                    set.Add(shortName);
                }
            }
            stopToLines[group.Key] = set.ToList();
        }

        return stopToLines;
    }

    private async Task<List<T>> LoadCsvFile<T>(string filePath)
    {
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("CSV file not found: {FilePath}", filePath);
            return new List<T>();
        }

        using var reader = new StreamReader(filePath);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        return csv.GetRecords<T>().ToList();
    }

    public async Task<List<StopInfo>> GetStopsAsync(string? stopId = null)
    {
        await InitializeAsync();

        var filteredStops = string.IsNullOrEmpty(stopId)
            ? _stops
            : _stops.Where(s => s.StopId.Equals(stopId, StringComparison.OrdinalIgnoreCase)).ToList();

        var result = filteredStops.Select(stop => new StopInfo
        {
            StopId = stop.StopId,
            StopCode = stop.StopCode,
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
        
        // If no calendar data is available (test scenario), return all stop times without filtering
        if (!_calendar.Any())
        {
            _logger.LogWarning("No calendar data available - returning all stop times without service filtering");
            return _stopTimes
                .Where(st => st.StopId.Equals(stopId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        
        // Get active service IDs for today
        var activeServiceIds = GetActiveServiceIds(DateTime.Now.Date);
        
        return _stopTimes
            .Where(st => st.StopId.Equals(stopId, StringComparison.OrdinalIgnoreCase))
            .Where(st => {
                var trip = _trips.FirstOrDefault(t => t.TripId == st.TripId);
                return trip != null && activeServiceIds.Contains(trip.ServiceId);
            })
            .ToList();
    }

    public async Task<Route?> GetRouteAsync(string routeId)
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
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<List<string>> GetTramLinesAsync()
    {
        await InitializeAsync();
        return _routes
            .Where(r => r.RouteType == 0 && !string.IsNullOrEmpty(r.RouteShortName))
            .Select(r => r.RouteShortName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<List<string>> GetBusLinesAsync()
    {
        await InitializeAsync();
        return _routes
            .Where(r => r.RouteType == 3 && !string.IsNullOrEmpty(r.RouteShortName))
            .Select(r => r.RouteShortName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private HashSet<string> GetActiveServiceIds(DateTime date)
    {
        var activeServices = new HashSet<string>();
        var dayOfWeek = date.DayOfWeek;
        
        foreach (var calendar in _calendar)
        {
            // Check if the service is active on this day of week
            bool isActiveToday = dayOfWeek switch
            {
                DayOfWeek.Monday => calendar.Monday == 1,
                DayOfWeek.Tuesday => calendar.Tuesday == 1,
                DayOfWeek.Wednesday => calendar.Wednesday == 1,
                DayOfWeek.Thursday => calendar.Thursday == 1,
                DayOfWeek.Friday => calendar.Friday == 1,
                DayOfWeek.Saturday => calendar.Saturday == 1,
                DayOfWeek.Sunday => calendar.Sunday == 1,
                _ => false
            };
            
            if (isActiveToday)
            {
                // Check if current date is within service period
                if (DateTime.TryParseExact(calendar.StartDate, "yyyyMMdd", null, DateTimeStyles.None, out var startDate) &&
                    DateTime.TryParseExact(calendar.EndDate, "yyyyMMdd", null, DateTimeStyles.None, out var endDate))
                {
                    if (date >= startDate.Date && date <= endDate.Date)
                    {
                        activeServices.Add(calendar.ServiceId);
                    }
                }
            }
        }
        
        _logger.LogInformation("Found {Count} active services for {Date} ({DayOfWeek}): {Services}", 
            activeServices.Count, date.ToShortDateString(), dayOfWeek, string.Join(", ", activeServices));
        
        return activeServices;
    }

    public async Task<List<Calendar>> GetCalendarDataAsync()
    {
        await InitializeAsync();
        return _calendar;
    }
}