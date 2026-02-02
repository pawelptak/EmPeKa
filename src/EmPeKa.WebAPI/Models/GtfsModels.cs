using CsvHelper.Configuration.Attributes;

namespace EmPeKa.Models;

public class Stop
{
    [Name("stop_id")]
    public required string StopId { get; set; }

    [Name("stop_code")]
    public required string StopCode { get; set; }

    [Name("stop_name")]
    public required string StopName { get; set; }
    
    [Name("stop_lat")]
    public double StopLat { get; set; }
    
    [Name("stop_lon")]
    public double StopLon { get; set; }
}

public class Route
{
    [Name("route_id")]
    public required string RouteId { get; set; }
    
    [Name("route_short_name")]
    public required string RouteShortName { get; set; }
    
    [Name("route_long_name")]
    public required string RouteLongName { get; set; }
    
    [Name("route_type")]
    public int RouteType { get; set; }
}

public class Trip
{
    [Name("trip_id")]
    public required string TripId { get; set; }
    
    [Name("route_id")]
    public required string RouteId { get; set; }
    
    [Name("service_id")]
    public required string ServiceId { get; set; }
    
    [Name("trip_headsign")]
    public string? TripHeadsign { get; set; }
    
    [Name("direction_id")]
    public int? DirectionId { get; set; }
}

public class StopTime
{
    [Name("trip_id")]
    public required string TripId { get; set; }
    
    [Name("arrival_time")]
    public required string ArrivalTime { get; set; }
    
    [Name("departure_time")]
    public required string DepartureTime { get; set; }
    
    [Name("stop_id")]
    public required string StopId { get; set; }
    
    [Name("stop_sequence")]
    public int StopSequence { get; set; }
}