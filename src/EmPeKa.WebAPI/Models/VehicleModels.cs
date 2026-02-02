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

// New MPK API models
public class MpkVehiclePosition
{
    public string Name { get; set; } = string.Empty; // Line name/number
    public string Type { get; set; } = string.Empty; // "tram" or "bus"
    public double Y { get; set; } // Longitude
    public double X { get; set; } // Latitude  
    public long K { get; set; } // Vehicle ID
}

// Legacy models (keeping for compatibility)
public class VehicleApiResponse
{
    public bool Success { get; set; }
    public VehicleApiResult Result { get; set; } = new();
}

public class VehicleApiResult
{
    public string ResourceId { get; set; } = string.Empty;
    public List<VehicleApiRecord> Records { get; set; } = new();
    public int Total { get; set; }
}

public class VehicleApiRecord
{
    public int _id { get; set; }
    public string? Nr_Boczny { get; set; }
    public string? Nr_Rej { get; set; }
    public string? Brygada { get; set; }
    public string? Nazwa_Linii { get; set; }
    public double Ostatnia_Pozycja_Szerokosc { get; set; }
    public double Ostatnia_Pozycja_Dlugosc { get; set; }
    public string? Data_Aktualizacji { get; set; }
}