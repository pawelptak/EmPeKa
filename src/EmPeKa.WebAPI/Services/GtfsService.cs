using CsvHelper;
using EmPeKa.Models;
using EmPeKa.WebAPI.Interfaces;
using System.Globalization;
using System.IO.Compression;
using Calendar = EmPeKa.Models.Calendar;
using Route = EmPeKa.Models.Route;

namespace EmPeKa.Services;

public class GtfsService : IGtfsService
{
    private readonly ILogger<GtfsService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _gtfsDataPath;

    private List<Stop> _stops = new();
    private List<Route> _routes = new();
    private List<Trip> _trips = new();
    private List<StopTime> _stopTimes = new();
    private List<Calendar> _calendar = new();

    // Pre-computed mapping for performance
    private Dictionary<string, List<string>> _stopToLinesMap = new();

    private DateTime _lastUpdate = DateTime.MinValue;
    private readonly TimeSpan _updateInterval = TimeSpan.FromHours(24); // Update daily
    private readonly SemaphoreSlim _initSemaphore = new(1, 1);

    public GtfsService(ILogger<GtfsService> logger, HttpClient httpClient, IConfiguration configuration)
    {
        _logger = logger;
        _httpClient = httpClient;
        _gtfsDataPath = configuration["GtfsDataPath"] ?? Path.Combine(Path.GetTempPath(), "gtfs_data");

        Directory.CreateDirectory(_gtfsDataPath);
    }

    public async Task InitializeAsync()
    {
        await _initSemaphore.WaitAsync();

        try
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

                // Force garbage collection after loading large datasets
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                _logger.LogInformation("GTFS data loaded successfully. Stops: {StopsCount}, Routes: {RoutesCount}, Trips: {TripsCount}, StopTimes: {StopTimesCount}",
                    _stops.Count, _routes.Count, _trips.Count, _stopTimes.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize GTFS data");

                throw;
            }
        }
        finally
        {
            _initSemaphore.Release();
        }
    }

    private async Task DownloadAndExtractGtfsData()
    {
        var gtfsUrl = await FindValidGtfsUrlAsync();

        _logger.LogInformation("Using GTFS URL: {Url}", gtfsUrl);

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

        await ZipFile.ExtractToDirectoryAsync(zipPath, extractPath);

        // Delete zip file after extraction
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }
    }

    private async Task<string> FindValidGtfsUrlAsync()
    {
        const string baseUrl = "https://www.wroclaw.pl/open-data/87b09b32-f076-4475-8ec9-6020ed1f9ac0/OtwartyWroclaw_rozklad_jazdy_GTFS_";
        const string urlSuffix = ".zip";
        var today = DateTime.Now.Date;
        var todayUrl = $"{baseUrl}{today:ddMMyyyy}{urlSuffix}";

        // Search for today's file first
        _logger.LogInformation("Checking GTFS URL for today ({Date}): {Url}", today.ToString("yyyy-MM-dd"), todayUrl);
        try
        {
            var response = await _httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, todayUrl));

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Found valid GTFS URL for today: {Url}", todayUrl);

                return todayUrl;
            }
            else
            {
                _logger.LogWarning("Today's URL returned {StatusCode}: {Url}", response.StatusCode, todayUrl);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Today's URL {Url} is not accessible: {Error}", todayUrl, ex.Message);
        }

        // If today's file not available, check 7 days into the past
        for (int i = 1; i <= 7; i++)
        {
            var date = today.AddDays(-i);
            var dateString = date.ToString("ddMMyyyy");
            var url = $"{baseUrl}{dateString}{urlSuffix}";

            _logger.LogInformation("Checking GTFS URL for past date ({Date}): {Url}", date.ToString("yyyy-MM-dd"), url);

            try
            {
                var response = await _httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Found valid GTFS URL for past date: {Url}", url);

                    return url;
                }
                else
                {
                    _logger.LogWarning("Past URL returned {StatusCode}: {Url}", response.StatusCode, url);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Past URL {Url} is not accessible: {Error}", url, ex.Message);
            }
        }

        // Fallback to a known working URL (update this manually if needed)
        var fallbackUrl = $"{baseUrl}02022026{urlSuffix}";

        _logger.LogError("No valid GTFS URL found, using fallback: {Url}", fallbackUrl);

        return fallbackUrl;
    }

    private async Task LoadGtfsData()
    {
        var extractPath = Path.Combine(_gtfsDataPath, "extracted");

        var stopsTask = LoadCsvFile<Stop>(Path.Combine(extractPath, "stops.txt"));
        var routesTask = LoadCsvFile<Route>(Path.Combine(extractPath, "routes.txt"));
        var tripsTask = LoadCsvFile<Trip>(Path.Combine(extractPath, "trips.txt"));
        var stopTimesTask = LoadCsvFile<StopTime>(Path.Combine(extractPath, "stop_times.txt"));
        var calendarTask = LoadCsvFile<Calendar>(Path.Combine(extractPath, "calendar.txt"));

        await Task.WhenAll(stopsTask, routesTask, tripsTask, stopTimesTask, calendarTask);

        _stops = stopsTask.Result;
        _routes = routesTask.Result;
        _trips = tripsTask.Result;
        _stopTimes = stopTimesTask.Result;
        _calendar = calendarTask.Result;

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

        // Group stop times by stop ID and collect unique lines using SortedSet for uniqueness and ordering
        var stopToLines = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in _stopTimes
            .Where(st => tripToRoute
            .ContainsKey(st.TripId))
            .GroupBy(st => st.StopId, StringComparer.OrdinalIgnoreCase))
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

        // Fast mapping using pre-computed dictionary
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

        // Get active service IDs for today
        var activeServiceIds = GetActiveServiceIds(DateTime.Now.Date);

        return _stopTimes
            .Where(st => st.StopId.Equals(stopId, StringComparison.OrdinalIgnoreCase))
            .Where(st =>
            {
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

    private HashSet<string> GetActiveServiceIds(DateTime date)
    {
        var activeServices = new HashSet<string>();
        var dayOfWeek = date.DayOfWeek;

        _logger.LogInformation("Looking for active services on {Date} ({DayOfWeek}), total calendar entries: {Count}",
            date.ToShortDateString(), dayOfWeek, _calendar.Count);

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
                    _logger.LogDebug("Service {ServiceId}: StartDate={StartDate}, EndDate={EndDate}, Current={CurrentDate}, InRange={InRange}",
                        calendar.ServiceId, startDate.ToShortDateString(), endDate.ToShortDateString(),
                        date.ToShortDateString(), date >= startDate.Date && date <= endDate.Date);

                    if (date >= startDate.Date && date <= endDate.Date)
                    {
                        activeServices.Add(calendar.ServiceId);
                    }
                }
                else
                {
                    _logger.LogWarning("Failed to parse dates for service {ServiceId}: StartDate='{StartDate}', EndDate='{EndDate}'",
                        calendar.ServiceId, calendar.StartDate, calendar.EndDate);
                }
            }
            else
            {
                _logger.LogDebug("Service {ServiceId} is not active on {DayOfWeek}", calendar.ServiceId, dayOfWeek);
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