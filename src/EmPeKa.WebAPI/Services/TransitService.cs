using EmPeKa.WebAPI.Models;
using EmPeKa.WebAPI.Interfaces;

namespace EmPeKa.WebAPI.Services;

public class TransitService : ITransitService
{
    private readonly IGtfsService _gtfsService;
    private readonly IVehicleService _vehicleService;
    private readonly ILogger<TransitService> _logger;

    public TransitService(IGtfsService gtfsService, IVehicleService vehicleService, ILogger<TransitService> logger)
    {
        _gtfsService = gtfsService;
        _vehicleService = vehicleService;
        _logger = logger;
    }

    public async Task<List<ArrivalInfo>> GetArrivalsForStopsAsync(IEnumerable<string> stopCodes, int countPerStop = 3)
    {
        var allArrivals = new List<ArrivalInfo>();

        // Materialize the enumeration to avoid multiple enumeration and to safely share across tasks.
        var stopCodeList = stopCodes?.ToList() ?? new List<string>();

        if (stopCodeList.Count == 0)
        {
            return allArrivals;
        }

        // Limit the number of concurrent requests to avoid overloading the upstream service.
        const int maxDegreeOfParallelism = 5;
        using var semaphore = new SemaphoreSlim(maxDegreeOfParallelism);

        var tasks = stopCodeList.Select(async stopCode =>
        {
            await semaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                var arrivalsResponse = await GetArrivalsAsync(stopCode, countPerStop).ConfigureAwait(false);
                if (arrivalsResponse != null && arrivalsResponse.Arrivals != null)
                {
                    lock (allArrivals)
                    {
                        allArrivals.AddRange(arrivalsResponse.Arrivals);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get arrivals for stopCode {StopCode}", stopCode);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        return allArrivals.OrderBy(a => a.EtaMin).ToList();
    }

    public async Task<ArrivalsResponse?> GetArrivalsAsync(string stopCode, int count = 3)
    {
        try
        {
            _logger.LogInformation("Getting arrivals for stopCode: {StopCode} (count={Count})", stopCode, count);

            // Get stop information by stopCode
            var allStops = await _gtfsService.GetStopsAsync();

            var stop = allStops.FirstOrDefault(s => s.StopCode == stopCode);

            if (stop == null)
            {
                _logger.LogWarning("Stop not found: {StopCode}", stopCode);

                return null;
            }

            // Get scheduled stop times by stopId
            var stopTimes = await _gtfsService.GetStopTimesForStopAsync(stop.StopId);
            var now = DateTime.Now;
            var currentTime = TimeSpan.Parse(now.ToString("HH:mm:ss"));

            _logger.LogInformation("Found {Count} total stop times for stop {StopCode} (StopId: {StopId})", stopTimes.Count, stopCode, stop.StopId);

            // Get next arrivals (no grouping)
            var upcomingStopTimes = stopTimes
                .Where(st => TimeSpan.TryParse(st.ArrivalTime, out var arrivalTime) && arrivalTime >= currentTime)
                .OrderBy(st => TimeSpan.Parse(st.ArrivalTime))
                .Take(20) // Limit to next 20 arrivals
                .ToList();

            _logger.LogInformation("Found {Count} upcoming stop times for stop {StopCode} (current time: {CurrentTime})", upcomingStopTimes.Count, stopCode, currentTime);


            var arrivals = new List<ArrivalInfo>();
            const double avgSpeedKmh = 20.0;
            const double avgSpeedMps = avgSpeedKmh * 1000.0 / 3600.0;
            const double closeDistanceMeters = 100.0;

            // Get tram lines
            var tramLines = upcomingStopTimes
                .Select(st =>
                {
                    var trip = _gtfsService.GetTripAsync(st.TripId).Result;
                    var route = trip != null ? _gtfsService.GetRouteAsync(trip.RouteId).Result : null;
                    return (route != null && route.RouteType == 0) ? route.RouteShortName : null;
                })
                .Where(x => x != null)
                .Distinct()
                .ToList();

            // Get bus lines
            var busLines = upcomingStopTimes
                .Select(st =>
                {
                    var trip = _gtfsService.GetTripAsync(st.TripId).Result;
                    var route = trip != null ? _gtfsService.GetRouteAsync(trip.RouteId).Result : null;
                    return (route != null && route.RouteType == 3) ? route.RouteShortName : null;
                })
                .Where(x => x != null)
                .Distinct()
                .ToList();

            var tramPositionsTask = tramLines.Count > 0 ? _vehicleService.GetVehiclesForLinesAsync(tramLines, "tram") : Task.FromResult(new List<VehiclePosition>());
            var busPositionsTask = busLines.Count > 0 ? _vehicleService.GetVehiclesForLinesAsync(busLines, "bus") : Task.FromResult(new List<VehiclePosition>());

            List<VehiclePosition> tramPositions;
            List<VehiclePosition> busPositions;

            try
            {
                await Task.WhenAll(tramPositionsTask, busPositionsTask);
                tramPositions = tramPositionsTask.Result;
                busPositions = busPositionsTask.Result;

                _logger.LogInformation("Retrieved {TramCount} tram positions and {BusCount} bus positions",
                    tramPositions.Count, busPositions.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to retrieve vehicle positions, using schedule-only data");
                tramPositions = new List<VehiclePosition>();
                busPositions = new List<VehiclePosition>();
            }

            foreach (var stopTime in upcomingStopTimes)
            {
                var trip = await _gtfsService.GetTripAsync(stopTime.TripId);
                if (trip == null) continue;

                var route = await _gtfsService.GetRouteAsync(trip.RouteId);
                if (route == null) continue;

                var arrivalTime = TimeSpan.Parse(stopTime.ArrivalTime);
                int etaMinSchedule = (int)Math.Max(0, (arrivalTime - currentTime).TotalMinutes);
                int etaMin = etaMinSchedule;
                bool isRealTime = false;
                VehiclePosition? vehicle = null;

                if (route.RouteType == 0)
                {
                    vehicle = tramPositions
                        .Where(p => string.Equals(p.NazwaLinii, route.RouteShortName, StringComparison.OrdinalIgnoreCase))
                        .Where(p => Haversine(stop.Latitude, stop.Longitude, p.OstatniaPositionSzerokosc, p.OstatniaPositionDlugosc) < 3000) // Max 3km radius
                        .OrderBy(p => Haversine(stop.Latitude, stop.Longitude, p.OstatniaPositionSzerokosc, p.OstatniaPositionDlugosc))
                        .FirstOrDefault();
                }
                else if (route.RouteType == 3)
                {
                    vehicle = busPositions
                        .Where(p => string.Equals(p.NazwaLinii, route.RouteShortName, StringComparison.OrdinalIgnoreCase))
                        .Where(p => Haversine(stop.Latitude, stop.Longitude, p.OstatniaPositionSzerokosc, p.OstatniaPositionDlugosc) < 5000) // Max 5km radius
                        .OrderBy(p => Haversine(stop.Latitude, stop.Longitude, p.OstatniaPositionSzerokosc, p.OstatniaPositionDlugosc))
                        .FirstOrDefault();
                }
                if (vehicle != null)
                {
                    double distance = Haversine(stop.Latitude, stop.Longitude, vehicle.OstatniaPositionSzerokosc, vehicle.OstatniaPositionDlugosc);

                    // Additional validation: check if distance calculation is reasonable
                    if (double.IsNaN(distance) || double.IsInfinity(distance))
                    {
                        _logger.LogWarning("Invalid distance calculation for vehicle {VehicleId} on line {Line} to stop {StopCode}",
                            vehicle.Id, route.RouteShortName, stopCode);
                        // Skip this vehicle and use schedule-only ETA
                    }
                    else
                    {
                        // Only set ETA = 0 if vehicle is very close and before schedule departure time
                        if (distance < closeDistanceMeters && etaMinSchedule <= 5)
                        {
                            etaMin = 0;
                            isRealTime = true;
                        }
                        else if (distance < 2000) // Only for vehicles within 2km, to avoid unrealistic ETAs
                        {
                            double etaSeconds = distance / avgSpeedMps;
                            int calculatedEta = Math.Max(1, (int)Math.Ceiling(etaSeconds / 60.0)); // Minimum 1 min

                            // Use real-time ETA only if it's reasonable compared to the schedule ETA
                            if (calculatedEta <= etaMinSchedule + 10 && calculatedEta >= Math.Max(1, etaMinSchedule - 3))
                            {
                                etaMin = calculatedEta;
                                isRealTime = true;
                            }
                        }
                    }
                }

                arrivals.Add(new ArrivalInfo
                {
                    Line = route.RouteShortName,
                    Direction = trip.TripHeadsign ?? "Unknown Direction",
                    EtaMin = etaMin,
                    IsRealTime = isRealTime,
                    ScheduledDeparture = stopTime.ArrivalTime
                });
            }

            // Deduplicate arrivals by Line, Direction, and ScheduledDeparture, then sort by ETA and take the top results
            arrivals = arrivals
                .GroupBy(a => new { a.Line, a.Direction, a.ScheduledDeparture })
                .Select(g => g.First())
                .OrderBy(a => a.EtaMin)
                .Take(count <= 0 ? 5 : count)
                .ToList();

            // Haversine formula for distance in meters
            static double Haversine(double lat1, double lon1, double lat2, double lon2)
            {
                double R = 6371000; // Earth radius in meters
                double dLat = (lat2 - lat1) * Math.PI / 180.0;
                double dLon = (lon2 - lon1) * Math.PI / 180.0;
                double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                           Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                           Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
                double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

                return R * c;
            }

            var response = new ArrivalsResponse
            {
                StopCode = stop.StopCode,
                StopName = stop.StopName,
                Arrivals = arrivals
            };

            _logger.LogInformation("Returning {Count} arrivals for stop {StopCode} (requested count: {RequestedCount})",
                arrivals.Count, stopCode, count);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get arrivals for stopCode {StopCode}", stopCode);

            return null;
        }
    }
}