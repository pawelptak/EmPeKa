using EmPeKa.Models;

namespace EmPeKa.WebAPI.Interfaces
{
    public interface ITransitService
    {
        Task<ArrivalsResponse?> GetArrivalsAsync(string stopCode, int count = 3);
    }
}
