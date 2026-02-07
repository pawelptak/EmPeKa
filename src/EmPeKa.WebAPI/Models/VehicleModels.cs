namespace EmPeKa.Models;

public class VehiclePosition
{
    public int Id { get; set; }
    public long VehicleNumber { get; set; }
    public string? LineName { get; set; }
    public double LastLatitude { get; set; }
    public double LastLongitude { get; set; }
    public DateTime LastUpdated { get; set; }
}

public class MpkVehiclePosition
{
    public string Name { get; set; } = string.Empty; // Line name/number
    public string Type { get; set; } = string.Empty; // "tram" or "bus"
    public double Y { get; set; } // Longitude
    public double X { get; set; } // Latitude  
    public long K { get; set; } // Vehicle ID
}
