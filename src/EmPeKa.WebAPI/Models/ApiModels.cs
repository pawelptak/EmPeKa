namespace EmPeKa.Models;

public class StopInfo
{
    public required string StopId { get; set; }
    public required string StopName { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public List<string> Lines { get; set; } = new();
}

public class ArrivalInfo
{
    public required string Line { get; set; }
    public required string Direction { get; set; }
    public int EtaMin { get; set; }
    public bool IsRealTime { get; set; }
    public string? ScheduledDeparture { get; set; } // Planowana godzina odjazdu (HH:mm:ss)
}

public class StopsResponse
{
    public List<StopInfo> Stops { get; set; } = new();
    public int Total { get; set; }
}

public class ArrivalsResponse
{
    public required string StopId { get; set; }
    public required string StopName { get; set; }
    public List<ArrivalInfo> Arrivals { get; set; } = new();
}