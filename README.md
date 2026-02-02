# EmPeKa - MPK Wroclaw API

Private API for retrieving public transport arrival times for MPK Wroclaw, using GTFS data and real-time vehicle positions.

## Features

- **GET /stops** - List of available stops with lines
- **GET /stops/{stopCode}/arrivals** - Upcoming arrivals for a given stop
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

### Get stop by stop code

```bash
curl "http://localhost:8080/stops?stopCode=123456"
```

### Get arrivals for a stop

```bash
curl http://localhost:8080/stops/123456/arrivals
```

Sample response:
```json
{
  "stopCode": "123456",
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

Open the frontend at `/` and provide `stopCode` query to select stop:

Examples:
- https://localhost:5001/?stopCode=123456
- http://localhost:5000/?stopCode=123456

## Monitoring

The API exposes a `/health` endpoint that returns:
- General API status
- Number of loaded GTFS stops
- Number of active real-time vehicles
- Last data update time
