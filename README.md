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

# Run the WebAPI
dotnet run --project EmPeKa.WebAPI

# Run the Frontend (ASP.NET Core MVC)
dotnet run --project EmPeka.Frontend
```

The applications will be available at:
- API (default): http://localhost:8080
- Scalar UI: http://localhost:8080/scalar/v1
- Frontend: https://localhost:5001 (or http://localhost:5000)

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
  "stopName": "Dworzec Główny",
  "arrivals": [
    {
      "line": "14",
      "direction": "FAT",
      "etaMin": 2,
      "isRealTime": true
    },
    {
      "line": "33",
      "direction": "Leśnica",
      "etaMin": 5,
      "isRealTime": false
    }
  ]
}
```

## Data Sources

1. **GTFS Wroclaw** - Timetable files (updated daily)
   - URL: https://www.wroclaw.pl/open-data/87b09b32-f076-4475-8ec9-6020ed1f9ac0/

2. **Real-time vehicle positions** - Current GPS positions of buses and trams (30s cache)
   - URL: https://www.wroclaw.pl/open-data/api/action/datastore_search?resource_id=a9b3841d-e977-474e-9e86-8789e470a85a

## Configuration

### appsettings.json

```json
{
  "GtfsDataPath": "./data/gtfs",
  "Urls": "http://0.0.0.0:8080",
  "EmPeKaApi": {
    "BaseUrl": "http://localhost:8080/"
  }
}
```

### Environment Variables

- `ASPNETCORE_ENVIRONMENT` - Environment (Development/Production)
- `ASPNETCORE_URLS` - URLs to listen on
- `GtfsDataPath` - Path to GTFS data
- `EmPeKaApi__BaseUrl` - Frontend base URL to WebAPI

### Frontend usage

Open the frontend at `/` and provide `stopId` query to select stop:

Examples:
- https://localhost:5001/?stopId=123456
- http://localhost:5000/?stopId=123456

## Monitoring

The API exposes a `/health` endpoint that returns:
- General API status
- Number of loaded GTFS stops
- Number of active real-time vehicles
- Last data update time
