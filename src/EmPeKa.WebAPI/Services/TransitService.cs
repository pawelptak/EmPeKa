using EmPeKa.Models;
using EmPeKa.WebAPI.Interfaces;

namespace EmPeKa.WebAPI.Services;

public class TransitService : ITransitService
{
    private const int MaxDelayMinutesToShow = 5;
    private const int MaxCachedPositions = 2;
    private const int MaxPositionAgeSeconds = 60;
    private const int MaxUpcomingStopTimes = 20;
    private const double TramMaxDistanceMeters = 3000;
    private const double BusMaxDistanceMeters = 5000;
    private const double RealTimeDistanceThresholdMeters = 2000;
    private const double CloseDistanceMeters = 100.0;
    private const double AverageSpeedKmh = 20.0;
    private const double AverageSpeedMps = AverageSpeedKmh * 1000.0 / 3600.0;
    private const int ArrivalNowWindowMinutes = 5;
    private const int EtaMaxDelayMinutes = 5;
    private const int EtaMaxEarlyMinutes = 5;
    private const int DefaultResultCount = 5;
    private const double ApproachingStopThresholdMeters = 10.0;

    private readonly IGtfsService _gtfsService;
    private readonly IVehicleService _vehicleService;
    private readonly ILogger<TransitService> _logger;

    private static readonly Dictionary<long, LinkedList<VehiclePosition>> _vehiclePositionCache = new();
    private static readonly object _vehicleCacheLock = new();

    public TransitService(IGtfsService gtfsService, IVehicleService vehicleService, ILogger<TransitService> logger)
    {
        _gtfsService = gtfsService;
        _vehicleService = vehicleService;
        _logger = logger;
    }

    public async Task<ArrivalsResponse?> GetArrivalsAsync(string stopCode, int count = 3)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stopCode);
        
        if (count < 0)
        {
            count = DefaultResultCount;
        }

        try
        {
            _logger.LogInformation("Getting arrivals for stopCode: {StopCode} (count={Count})", stopCode, count);

            // Stop information
            var allStops = await _gtfsService.GetStopsAsync();
            var stop = allStops.FirstOrDefault(s => s.StopCode == stopCode);
            if (stop == null)
            {
                _logger.LogWarning("Stop not found: {StopCode}", stopCode);
                return null;
            }

            // Stop times
            var stopTimes = await _gtfsService.GetStopTimesForStopAsync(stop.StopId);
            var now = DateTime.Now;

            var upcomingStopTimes = stopTimes
                .Select(st =>
                {
                    if (!TimeSpan.TryParse(st.ArrivalTime, out var arrivalTime))
                    {
                        return (st: st, arrivalDateTime: (DateTime?)null);
                    }

                    var dt = DateTime.Today.Add(arrivalTime);
                    if (dt < now - TimeSpan.FromMinutes(MaxDelayMinutesToShow))
                        dt = dt.AddDays(1);

                    return (st: st, arrivalDateTime: (DateTime?)dt);
                })
                .Where(x => x.arrivalDateTime.HasValue &&
                            x.arrivalDateTime.Value >= now - TimeSpan.FromMinutes(MaxDelayMinutesToShow))
                .OrderBy(x => x.arrivalDateTime)
                .Take(MaxUpcomingStopTimes)
                .Select(x => x.st)
                .ToList();

            // Batch load all trips and routes once
            var allTripIds = upcomingStopTimes.Select(st => st.TripId).Distinct().ToList();
            var allTrips = await Task.WhenAll(allTripIds.Select(id => _gtfsService.GetTripAsync(id)));
            var tripDict = allTripIds.Zip(allTrips, (id, trip) => (id, trip)).ToDictionary(x => x.id, x => x.trip);

            var allRouteIds = allTrips.Where(trip => trip != null).Select(trip => trip!.RouteId).Distinct().ToList();
            var allRoutes = await Task.WhenAll(allRouteIds.Select(id => _gtfsService.GetRouteAsync(id)));
            var routeDict = allRouteIds.Zip(allRoutes, (id, route) => (id, route)).ToDictionary(x => x.id, x => x.route);

            // Extract tram and bus lines
            var tramLines = new List<string>();
            var busLines = new List<string>();

            foreach (var st in upcomingStopTimes)
            {
                if (!tripDict.TryGetValue(st.TripId, out var trip) || trip == null)
                    continue;

                if (!routeDict.TryGetValue(trip.RouteId, out var route) || route == null)
                    continue;

                if (route.RouteType == 0 && !string.IsNullOrEmpty(route.RouteShortName))
                {
                    tramLines.Add(route.RouteShortName);
                }
                else if (route.RouteType == 3 && !string.IsNullOrEmpty(route.RouteShortName))
                {
                    busLines.Add(route.RouteShortName);
                }
            }

            tramLines = tramLines.Distinct().ToList();
            busLines = busLines.Distinct().ToList();

            var tramPositionsTask = tramLines.Count > 0 
                ? _vehicleService.GetVehiclesForLinesAsync(tramLines, "tram") 
                : Task.FromResult(new List<VehiclePosition>());
            var busPositionsTask = busLines.Count > 0 
                ? _vehicleService.GetVehiclesForLinesAsync(busLines, "bus") 
                : Task.FromResult(new List<VehiclePosition>());

            List<VehiclePosition> tramPositions;
            List<VehiclePosition> busPositions;

            try
            {
                await Task.WhenAll(tramPositionsTask, busPositionsTask);
                tramPositions = tramPositionsTask.Result;
                busPositions = busPositionsTask.Result;

                UpdateVehicleCache(tramPositions);
                UpdateVehicleCache(busPositions);

                _logger.LogInformation("Retrieved {TramCount} tram positions and {BusCount} bus positions",
                    tramPositions.Count, busPositions.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to retrieve vehicle positions, using schedule-only data");
                tramPositions = new List<VehiclePosition>();
                busPositions = new List<VehiclePosition>();
            }

            var arrivals = new List<ArrivalInfo>();

            foreach (var stopTime in upcomingStopTimes)
            {
                if (!tripDict.TryGetValue(stopTime.TripId, out var trip) || trip == null)
                    continue;

                if (!routeDict.TryGetValue(trip.RouteId, out var route) || route == null)
                    continue;

                if (!TimeSpan.TryParse(stopTime.ArrivalTime, out var arrivalTime))
                {
                    _logger.LogWarning("Skipping stopTime with invalid ArrivalTime: {ArrivalTime} (TripId: {TripId})", stopTime.ArrivalTime, stopTime.TripId);
                    continue;
                }

                var scheduledDateTime = DateTime.Today.Add(arrivalTime);
                if (scheduledDateTime < now)
                    scheduledDateTime = scheduledDateTime.AddDays(1);

                int etaMinSchedule = (int)Math.Max(0, (scheduledDateTime - now).TotalMinutes);
                int etaMin = etaMinSchedule;
                bool isRealTime = false;
                int? delayMin = null;
                VehiclePosition? vehicle = null;

                // Find vehicle
                if (route.RouteType == 0)
                {
                    vehicle = FindClosestVehicle(tramPositions, route.RouteShortName, stop, TramMaxDistanceMeters);
                }
                else if (route.RouteType == 3)
                {
                    vehicle = FindClosestVehicle(busPositions, route.RouteShortName, stop, BusMaxDistanceMeters);
                }

                // Real-time ETA
                if (vehicle != null && IsApproachingStop(vehicle, stop))
                {
                    double distance = Haversine(stop.Latitude, stop.Longitude, vehicle.LastLatitude, vehicle.LastLongitude);
                    if (!double.IsNaN(distance) && !double.IsInfinity(distance))
                    {
                        if (distance < CloseDistanceMeters && etaMinSchedule <= ArrivalNowWindowMinutes)
                        {
                            etaMin = 0;
                            isRealTime = true;
                        }
                        else if (distance < RealTimeDistanceThresholdMeters)
                        {
                            double etaSeconds = distance / AverageSpeedMps;
                            int calculatedEta = Math.Max(1, (int)Math.Ceiling(etaSeconds / 60.0));

                            if (calculatedEta <= etaMinSchedule + EtaMaxDelayMinutes && 
                                calculatedEta >= Math.Max(1, etaMinSchedule - EtaMaxEarlyMinutes))
                            {
                                etaMin = calculatedEta;
                                isRealTime = true;
                            }
                        }
                    }
                }

                // Delay (positive = late, negative = early)
                if (isRealTime)
                {
                    double diff = etaMin - etaMinSchedule;
                    // Show delay/earliness but cap extreme values for display
                    if (Math.Abs(diff) >= 1.0)
                    {
                        delayMin = (int)Math.Round(diff);
                    }
                }

                arrivals.Add(new ArrivalInfo
                {
                    Line = route.RouteShortName,
                    Direction = trip.TripHeadsign ?? "Unknown Direction",
                    EtaMin = etaMin,
                    IsRealTime = isRealTime,
                    DelayMin = delayMin,
                    ScheduledDeparture = stopTime.ArrivalTime
                });
            }

            // Deduplicate and take top results
            arrivals = arrivals
                .GroupBy(a => new { a.Line, a.Direction, a.ScheduledDeparture })
                .Select(g => g.First())
                .OrderBy(a => a.EtaMin)
                .Take(count <= 0 ? DefaultResultCount : count)
                .ToList();

            return new ArrivalsResponse
            {
                StopCode = stop.StopCode,
                StopName = stop.StopName,
                Arrivals = arrivals
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get arrivals for stopCode {StopCode}", stopCode);
            return null;
        }
    }

    private static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        double R = 6371000;
        double dLat = (lat2 - lat1) * Math.PI / 180.0;
        double dLon = (lon2 - lon1) * Math.PI / 180.0;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private void UpdateVehicleCache(IEnumerable<VehiclePosition> positions)
    {
        lock (_vehicleCacheLock)
        {
            foreach (var pos in positions)
            {
                if (!_vehiclePositionCache.TryGetValue(pos.VehicleNumber, out var list))
                {
                    list = new LinkedList<VehiclePosition>();
                    _vehiclePositionCache[pos.VehicleNumber] = list;
                }

                if (list.Last != null && list.Last.Value.LastUpdated == pos.LastUpdated)
                    continue;

                list.AddLast(pos);
                while (list.Count > MaxCachedPositions)
                    list.RemoveFirst();
            }
        }
    }

    private bool IsApproachingStop(VehiclePosition current, StopInfo stop)
    {
        lock (_vehicleCacheLock)
        {
            if (!_vehiclePositionCache.TryGetValue(current.VehicleNumber, out var history))
                return false;
            if (history.Count < 2)
                return false;

            var prevNode = history.Last!.Previous;
            if (prevNode == null)
                return false;
            var prev = prevNode.Value;
            var ageSeconds = (DateTime.Now - current.LastUpdated).TotalSeconds;
            if (ageSeconds > MaxPositionAgeSeconds)
                return false;

            double prevDist = Haversine(stop.Latitude, stop.Longitude, prev.LastLatitude, prev.LastLongitude);
            double currDist = Haversine(stop.Latitude, stop.Longitude, current.LastLatitude, current.LastLongitude);

            // Vehicle must be getting closer (with threshold for GPS noise)
            if (currDist + ApproachingStopThresholdMeters >= prevDist)
                return false;

            // Additional check: vehicle shouldn't be too close already (likely passed the stop)
            // If very close but not approaching fast enough, it might have already stopped
            if (currDist < 50 && (prevDist - currDist) < 5)
                return false;

            // Check if movement direction is generally towards the stop
            // Calculate bearing to stop from both positions
            double timeDiff = (current.LastUpdated - prev.LastUpdated).TotalSeconds;
            if (timeDiff > 0)
            {
                double distanceTraveled = Haversine(prev.LastLatitude, prev.LastLongitude, 
                    current.LastLatitude, current.LastLongitude);
                double approachRate = (prevDist - currDist) / timeDiff;
                
                // Vehicle should be approaching at reasonable rate (not just drifting)
                // At least 2 m/s (~7 km/h) approach rate to filter out stationary vehicles
                if (approachRate < 2.0)
                    return false;
            }

            return true;
        }
    }

    private static VehiclePosition? FindClosestVehicle(
        List<VehiclePosition> positions,
        string lineName,
        StopInfo stop,
        double maxDistanceMeters)
    {
        VehiclePosition? closestVehicle = null;
        double closestDistance = double.MaxValue;

        foreach (var position in positions)
        {
            if (!string.Equals(position.LineName, lineName, StringComparison.OrdinalIgnoreCase))
                continue;

            double distance = Haversine(stop.Latitude, stop.Longitude, position.LastLatitude, position.LastLongitude);
            
            if (distance < maxDistanceMeters && distance < closestDistance)
            {
                closestDistance = distance;
                closestVehicle = position;
            }
        }

        return closestVehicle;
    }
}
