# EmPeKa - MPK Wroclaw API

Private API for retrieving public transport arrival times for MPK Wroclaw, using GTFS data and real-time vehicle positions.

## Features

- **GET /stops** - List of available stops with lines
- **GET /stops/{stopId}/arrivals** - Upcoming arrivals for a given stop
- **GET /health** - API status and data availability

## Getting Started

### Locally (Development)

```bash
# Clone the repository and enter the directory
cd EmPeKa

# Restore NuGet packages
dotnet restore

# Run the application
dotnet run
```

The application will be available at:
- API: https://localhost:5001
- Scalar UI: https://localhost:5001/scalar/v1

### Docker (Production)

```bash
# Build and run the container
docker-compose up -d

# or manually:
docker build -t empeka-api .
docker run -d -p 8080:8080 --name empeka-api empeka-api
```

The application will be available at:
- API: http://localhost:8080
- Scalar UI: http://localhost:8080/scalar/v1
- Health Check: http://localhost:8080/health

## Usage Examples

### Get all stops

```bash
curl http://localhost:8080/stops
```

### Get stop by ID

```bash
curl "http://localhost:8080/stops?stopId=123456"
```

### Get arrivals for a stop

```bash
curl http://localhost:8080/stops/123456/arrivals
```

Sample response:
```json
{
  "stopId": "123456",
  "stopName": "Dworzec G³ówny",
  "arrivals": [
    {
      "line": "14",
      "direction": "FAT",
      "etaMin": 2,
      "isRealTime": true
    },
    {
      "line": "33",
      "direction": "Leœnica",
      "etaMin": 5,
      "isRealTime": false
    }
  ]
}
```

## Data Sources

1. **GTFS Wroc³aw** - Timetable files (updated daily)
   - URL: https://www.wroclaw.pl/open-data/87b09b32-f076-4475-8ec9-6020ed1f9ac0/

2. **Real-time vehicle positions** - Current GPS positions of buses and trams (30s cache)
   - URL: https://www.wroclaw.pl/open-data/api/action/datastore_search?resource_id=a9b3841d-e977-474e-9e86-8789e470a85a

## Configuration

### appsettings.json

```json
{
  "GtfsDataPath": "./data/gtfs",
  "Urls": "http://0.0.0.0:8080"
}
```

### Environment Variables

- `ASPNETCORE_ENVIRONMENT` - Environment (Development/Production)
- `ASPNETCORE_URLS` - URLs to listen on
- `GtfsDataPath` - Path to GTFS data

## Architecture

- **Controllers** - API controllers (`StopsController`, `HealthController`)
- **Services** - Business logic (`GtfsService`, `VehicleService`, `TransitService`)
- **Models** - Data models (GTFS, API responses, vehicle positions)
- **Caching** - In-memory cache for vehicle positions (30s TTL)
- **Background updates** - Automatic GTFS data updates (every 24h)

## System Requirements

- .NET 10 (or Docker)
- ~100MB disk space for GTFS data
- Internet access (for data download)

## Monitoring

The API exposes a `/health` endpoint that returns:
- General API status
- Number of loaded GTFS stops
- Number of active real-time vehicles
- Last data update time
