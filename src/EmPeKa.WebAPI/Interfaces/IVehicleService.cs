using EmPeKa.Models;

namespace EmPeKa.WebAPI.Interfaces
{
    public interface IVehicleService
    {
        Task<List<VehiclePosition>> GetVehiclePositionsAsync();
        Task<List<VehiclePosition>> GetVehiclesForLineAsync(string line);
        Task<List<VehiclePosition>> GetVehiclesForLinesAsync(IEnumerable<string> lines, string vehicleType);
    }
}
