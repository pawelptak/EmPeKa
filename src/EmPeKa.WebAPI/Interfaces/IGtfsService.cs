using EmPeKa.WebAPI.Models;
using Route = EmPeKa.WebAPI.Models.Route;

namespace EmPeKa.WebAPI.Interfaces
{
    public interface IGtfsService
    {
        Task InitializeAsync();
        Task<List<StopInfo>> GetStopsAsync(string? stopId = null);
        Task<List<StopTime>> GetStopTimesForStopAsync(string stopId);
        Task<Route?> GetRouteAsync(string routeId);
        Task<Trip?> GetTripAsync(string tripId);
        Task<List<string>> GetAllLinesAsync();
        Task<List<string>> GetTramLinesAsync();
        Task<List<string>> GetBusLinesAsync();
        Task<List<Calendar>> GetCalendarDataAsync(); // Debug method
    }
}
