namespace EmPeKa.Models;

public class VehiclePosition
{
    public int Id { get; set; }
    public long NrBoczny { get; set; }
    public string? NrRej { get; set; }
    public string? Brygada { get; set; }
    public string? NazwaLinii { get; set; }
    public double OstatniaPositionSzerokosc { get; set; }
    public double OstatniaPositionDlugosc { get; set; }
    public DateTime DataAktualizacji { get; set; }
}

public class MpkVehiclePosition
{
    public string Name { get; set; } = string.Empty; // Line name/number
    public string Type { get; set; } = string.Empty; // "tram" or "bus"
    public double Y { get; set; } // Longitude
    public double X { get; set; } // Latitude  
    public long K { get; set; } // Vehicle ID
}
