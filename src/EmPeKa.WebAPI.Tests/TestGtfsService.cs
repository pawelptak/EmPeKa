namespace EmPeKa.WebAPI.Tests
{
    using EmPeKa.Services;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Configuration;
    using EmPeKa.Models;
    using CsvHelper;
    using System.Globalization;

    /// <summary>
    /// Test-specific implementation of GtfsService that uses local test data without downloading
    /// </summary>
    public class TestGtfsService : IGtfsService
    {
        private readonly ILogger<GtfsService> _logger;
        private readonly string _gtfsDataPath;
        
        private List<Stop> _stops = new();
        private List<EmPeKa.Models.Route> _routes = new();
        private List<Trip> _trips = new();
        private List<StopTime> _stopTimes = new();
        private List<EmPeKa.Models.Calendar> _calendar = new();
        
        // Pre-computed mapping for performance
        private Dictionary<string, List<string>> _stopToLinesMap = new();
        
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
            _routes = await LoadCsvFile<EmPeKa.Models.Route>(Path.Combine(extractPath, "routes.txt"));
            _trips = await LoadCsvFile<Trip>(Path.Combine(extractPath, "trips.txt"));
            _stopTimes = await LoadCsvFile<StopTime>(Path.Combine(extractPath, "stop_times.txt"));
            _calendar = await LoadCsvFile<EmPeKa.Models.Calendar>(Path.Combine(extractPath, "calendar.txt"));

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
            
            return _stopTimes
                .Where(st => st.StopId.Equals(stopId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public async Task<EmPeKa.Models.Route?> GetRouteAsync(string routeId)
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
    }
}