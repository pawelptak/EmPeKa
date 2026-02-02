using EmPeKa.Models;

namespace EmPeKa.Services;

public interface ITransitService
{
    Task<ArrivalsResponse?> GetArrivalsAsync(string stopCode, int count = 3);
}

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

    public async Task<ArrivalsResponse?> GetArrivalsAsync(string stopCode, int count = 3)
    {
        try
        {
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

            // Get next arrivals (no grouping)
            var upcomingStopTimes = stopTimes
                .Where(st => TimeSpan.TryParse(st.ArrivalTime, out var arrivalTime) && arrivalTime >= currentTime)
                .OrderBy(st => TimeSpan.Parse(st.ArrivalTime))
                .Take(20) // Limit to next 20 arrivals
                .ToList();


            // Ulepszony algorytm arrivals
            var arrivals = new List<ArrivalInfo>();
            const double avgSpeedKmh = 20.0;
            const double avgSpeedMps = avgSpeedKmh * 1000.0 / 3600.0;
            const double closeDistanceMeters = 100.0;

            // Zbierz linie tramwajowe i autobusowe
            var tramLines = upcomingStopTimes
                .Select(st => {
                    var trip = _gtfsService.GetTripAsync(st.TripId).Result;
                    var route = trip != null ? _gtfsService.GetRouteAsync(trip.RouteId).Result : null;
                    return (route != null && route.RouteType == 0) ? route.RouteShortName : null;
                })
                .Where(x => x != null)
                .Distinct()
                .ToList();
            var busLines = upcomingStopTimes
                .Select(st => {
                    var trip = _gtfsService.GetTripAsync(st.TripId).Result;
                    var route = trip != null ? _gtfsService.GetRouteAsync(trip.RouteId).Result : null;
                    return (route != null && route.RouteType == 3) ? route.RouteShortName : null;
                })
                .Where(x => x != null)
                .Distinct()
                .ToList();

            var tramPositionsTask = tramLines.Count > 0 ? _vehicleService.GetVehiclesForLinesAsync(tramLines, "tram") : Task.FromResult(new List<VehiclePosition>());
            var busPositionsTask = busLines.Count > 0 ? _vehicleService.GetVehiclesForLinesAsync(busLines, "bus") : Task.FromResult(new List<VehiclePosition>());
            await Task.WhenAll(tramPositionsTask, busPositionsTask);
            var tramPositions = tramPositionsTask.Result;
            var busPositions = busPositionsTask.Result;

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
                // Lepsze dopasowanie pojazdu: linia + kierunek
                if (route.RouteType == 0)
                {
                    vehicle = tramPositions
                        .Where(p => string.Equals(p.NazwaLinii, route.RouteShortName, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(p => Haversine(stop.Latitude, stop.Longitude, p.OstatniaPositionSzerokosc, p.OstatniaPositionDlugosc))
                        .FirstOrDefault();
                }
                else if (route.RouteType == 3)
                {
                    vehicle = busPositions
                        .Where(p => string.Equals(p.NazwaLinii, route.RouteShortName, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(p => Haversine(stop.Latitude, stop.Longitude, p.OstatniaPositionSzerokosc, p.OstatniaPositionDlugosc))
                        .FirstOrDefault();
                }
                if (vehicle != null)
                {
                    double distance = Haversine(stop.Latitude, stop.Longitude, vehicle.OstatniaPositionSzerokosc, vehicle.OstatniaPositionDlugosc);
                    if (distance < closeDistanceMeters)
                    {
                        etaMin = 0;
                    }
                    else
                    {
                        double etaSeconds = distance / avgSpeedMps;
                        etaMin = Math.Max(0, (int)Math.Ceiling(etaSeconds / 60.0));
                        // Nie pokazuj ETA real-time szybszego ni¿ rozk³ad
                        if (etaMin < etaMinSchedule)
                            etaMin = etaMinSchedule;
                    }
                    isRealTime = true;
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

            // Deduplikacja po Line, Direction, ScheduledDeparture, sortowanie po ETA i count
            arrivals = arrivals
                .GroupBy(a => new { a.Line, a.Direction, a.ScheduledDeparture })
                .Select(g => g.First())
                .OrderBy(a => a.EtaMin)
                .Take(count > 0 ? count : 3)
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
}