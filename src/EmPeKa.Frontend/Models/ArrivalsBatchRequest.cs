namespace EmPeKa.Frontend.Models
{
    public class ArrivalsBatchRequest
    {
        public List<string> StopCodes { get; set; } = new();
        public int CountPerStop { get; set; } = 5;
    }
}
