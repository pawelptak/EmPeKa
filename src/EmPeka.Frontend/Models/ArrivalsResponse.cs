namespace EmPeka.Frontend.Models;

public class ArrivalsResponse
{
    public string StopCode { get; set; } = string.Empty;
    public string StopName { get; set; } = string.Empty;
    public List<ArrivalItem> Arrivals { get; set; } = new();
}

public class ArrivalItem
{
    public string Line { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public int EtaMin { get; set; }
    public bool IsRealTime { get; set; }
    public string? ScheduledDeparture { get; set; } // Planowy odjazd (hh:mm:ss)
}
