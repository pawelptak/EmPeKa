using EmPeKa.WebAPI.Models;

namespace EmPeKa.WebAPI.Interfaces
{
    public interface ITransitService
    {
        Task<ArrivalsResponse?> GetArrivalsAsync(string stopCode, int count);
        Task<List<ArrivalInfo>> GetArrivalsForStopsAsync(IEnumerable<string> stopCodes, int countPerStop);
    }
}
