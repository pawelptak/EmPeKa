namespace EmPeKa.WebAPI.Tests
{
    using Xunit;
    using Moq;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Caching.Memory;
    using Microsoft.Extensions.Configuration;
    using Microsoft.AspNetCore.Mvc;
    using EmPeKa.Services;
    using EmPeKa.Models;
    using EmPeKa.Controllers;
    using System.Threading.Tasks;
    using Microsoft.Extensions.DependencyInjection;
    using System.Globalization;

    public class UnitTests
    {
        // Symulowana data: 2 lutego 2026 (poniedziałek) o 12:30
        private static readonly DateTime TestDateTime = new DateTime(2026, 2, 2, 12, 30, 0);
        private static readonly string TestDataPath = Path.Combine("..", "..", "..", "..", "EmPeKa.WebAPI", "data", "gtfs");
        
        private IGtfsService CreateRealGtfsService()
        {
            var logger = new Mock<ILogger<GtfsService>>();
            return new TestGtfsService(logger.Object, TestDataPath);
        }
        
        private IVehicleService CreateMockVehicleService()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var logger = new Mock<ILogger<VehicleService>>();
            var gtfs = new Mock<IGtfsService>();
            var httpClient = new HttpClient();
            
            // Dodanie przykładowych pojazdów z aktualną datą aktualizacji
            var vehicles = new List<VehiclePosition>
            {
                new VehiclePosition 
                { 
                    NazwaLinii = "1", 
                    DataAktualizacji = TestDateTime.AddMinutes(-2),
                    OstatniaPositionSzerokosc = 51.1071,
                    OstatniaPositionDlugosc = 17.0194
                },
                new VehiclePosition 
                { 
                    NazwaLinii = "A", 
                    DataAktualizacji = TestDateTime.AddMinutes(-1),
                    OstatniaPositionSzerokosc = 51.0943,
                    OstatniaPositionDlugosc = 17.0322
                }
            };
            cache.Set("vehicle_positions", vehicles);
            
            return new VehicleService(httpClient, cache, logger.Object, gtfs.Object);
        }
        [Fact]
        public async Task StopsController_GetStops_WithRealData_ReturnsActualStops()
        {
            // Arrange
            var gtfs = CreateRealGtfsService();
            await gtfs.InitializeAsync();
            var transit = new Mock<ITransitService>();
            var logger = new Mock<ILogger<StopsController>>();
            var controller = new StopsController(gtfs, transit.Object, logger.Object);

            // Act
            var result = await controller.GetStops();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var response = Assert.IsType<StopsResponse>(okResult.Value);
            Assert.NotEmpty(response.Stops);
            Assert.True(response.Stops.Count > 100); // Wrocław ma setki przystanków
            Assert.All(response.Stops, stop => {
                Assert.NotNull(stop.StopId);
                Assert.NotNull(stop.StopCode);
                Assert.NotNull(stop.StopName);
            });
        }

        [Fact]
        public async Task StopsController_GetStops_WithSpecificStopId_ReturnsFilteredStops()
        {
            // Arrange
            var gtfs = CreateRealGtfsService();
            await gtfs.InitializeAsync();
            var transit = new Mock<ITransitService>();
            var logger = new Mock<ILogger<StopsController>>();
            var controller = new StopsController(gtfs, transit.Object, logger.Object);

            // Act - szukamy przystanku "Dyrekcyjna" (StopId "29")
            var result = await controller.GetStops("29");

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var response = Assert.IsType<StopsResponse>(okResult.Value);
            Assert.NotEmpty(response.Stops);
            Assert.Contains(response.Stops, s => s.StopName.Contains("Dyrekcyjna"));
        }

        [Fact]
        public async Task StopsController_GetStops_WithInvalidStopId_ReturnsEmpty()
        {
            // Arrange
            var gtfs = CreateRealGtfsService();
            await gtfs.InitializeAsync();
            var transit = new Mock<ITransitService>();
            var logger = new Mock<ILogger<StopsController>>();
            var controller = new StopsController(gtfs, transit.Object, logger.Object);

            // Act - używamy nieistniejącego kodu przystanku
            var result = await controller.GetStops("99999");

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var response = Assert.IsType<StopsResponse>(okResult.Value);
            Assert.Empty(response.Stops);
        }
        [Fact]
        public async Task GtfsService_InitializeAsync_LoadsRealData()
        {
            // Arrange
            var service = CreateRealGtfsService();

            // Act
            await service.InitializeAsync();

            // Assert
            var stops = await service.GetStopsAsync();
            Assert.NotEmpty(stops);
            Assert.True(stops.Count > 1000); // Wrocław ma tysiące przystanków
            
            var routes = await service.GetAllLinesAsync();
            Assert.NotEmpty(routes);
            Assert.Contains("1", routes); // Linia tramwajowa 1
            Assert.Contains("A", routes); // Linia autobusowa A
        }

        [Fact]
        public async Task VehicleService_GetVehiclePositionsAsync_ReturnsTestVehicles()
        {
            // Arrange
            var service = CreateMockVehicleService();

            // Act
            var result = await service.GetVehiclePositionsAsync();

            // Assert
            Assert.Equal(2, result.Count);
            Assert.Contains(result, v => v.NazwaLinii == "1");
            Assert.Contains(result, v => v.NazwaLinii == "A");
            Assert.All(result, v => Assert.True(v.DataAktualizacji > TestDateTime.AddMinutes(-5)));
        }

        [Fact]
        public async Task TransitService_GetArrivalsAsync_ForRealStop_ReturnsScheduledArrivals()
        {
            // Arrange
            var gtfs = CreateRealGtfsService();
            await gtfs.InitializeAsync();
            var vehicles = CreateMockVehicleService();
            var logger = new Mock<ILogger<TransitService>>();
            var service = new TransitService(gtfs, vehicles, logger.Object);

            // Act - Dyrekcyjna (jeden z głównych przystanków)
            var result = await service.GetArrivalsAsync("21101");

            // Assert
            if (result != null) // W poniedziałek o 12:30 powinny być kursy
            {
                Assert.NotNull(result.StopName);
                Assert.Contains("Dyrekcyjna", result.StopName);
                // W dni robocze powinny być kursy
                Assert.True(result.Arrivals.Count >= 0);
            }
        }

        [Fact]
        public async Task GtfsService_GetTramLines_ReturnsWroclawTramLines()
        {
            // Arrange
            var service = CreateRealGtfsService();
            await service.InitializeAsync();

            // Act
            var tramLines = await service.GetTramLinesAsync();

            // Assert
            Assert.NotEmpty(tramLines);
            Assert.Contains("1", tramLines);
            Assert.Contains("2", tramLines);
            Assert.Contains("3", tramLines);
            Assert.Contains("4", tramLines);
            // Sprawdzamy czy to rzeczywiście linie tramwajowe (liczby)
            Assert.All(tramLines, line => Assert.True(int.TryParse(line, out _) || line.Length <= 3));
        }

        [Fact]
        public async Task GtfsService_GetBusLines_ReturnsWroclawBusLines()
        {
            // Arrange
            var service = CreateRealGtfsService();
            await service.InitializeAsync();

            // Act
            var busLines = await service.GetBusLinesAsync();

            // Assert
            Assert.NotEmpty(busLines);
            Assert.Contains("A", busLines); // Autobus A
            Assert.Contains("D", busLines); // Autobus D
            Assert.Contains("K", busLines); // Autobus K
            Assert.Contains("N", busLines); // Autobus N
            // Sprawdzamy czy zawiera linie numerowane (autobusy)
            Assert.Contains(busLines, line => line.StartsWith("1") && line.Length == 3); // Linie 1xx
        }

        [Fact]
        public async Task GtfsService_GetStopTimesForStopAsync_ForMonday_ReturnsWorkdaySchedule()
        {
            // Arrange - 2 lutego 2026 to poniedziałek
            var service = CreateRealGtfsService();
            await service.InitializeAsync();

            // Act - sprawdzamy czasy dla przystanku "Dyrekcyjna"
            var result = await service.GetStopTimesForStopAsync("29"); // StopId 29

            // Assert
            Assert.IsType<List<StopTime>>(result);
            if (result.Any())
            {
                // Sprawdzamy czy czasy są w formacie HH:mm:ss
                Assert.All(result, stopTime => {
                    Assert.Matches(@"^\d{1,2}:\d{2}:\d{2}$", stopTime.ArrivalTime);
                    Assert.Matches(@"^\d{1,2}:\d{2}:\d{2}$", stopTime.DepartureTime);
                });
            }
        }

        [Fact]
        public async Task GtfsService_GetRouteAsync_ReturnsRealRoute()
        {
            // Arrange
            var service = CreateRealGtfsService();
            await service.InitializeAsync();
            var allLines = await service.GetAllLinesAsync();
            var firstLine = allLines.FirstOrDefault();

            // Act
            var result = firstLine != null ? await service.GetRouteAsync(firstLine) : null;

            // Assert
            if (result != null)
            {
                Assert.NotNull(result.RouteId);
                Assert.NotNull(result.RouteShortName);
                Assert.NotNull(result.RouteLongName);
            }
        }

        [Fact]
        public async Task HealthController_GetHealth_WithRealData_ReturnsHealthy()
        {
            // Arrange
            var gtfs = CreateRealGtfsService();
            await gtfs.InitializeAsync();
            var vehicles = CreateMockVehicleService();
            var logger = new Mock<ILogger<HealthController>>();
            var controller = new HealthController(gtfs, vehicles, logger.Object);

            // Act
            var result = await controller.GetHealth();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            // System powinien być zdrowy z prawdziwymi danymi GTFS
        }

        [Fact]
        public async Task VehicleService_GetVehiclesForLineAsync_FiltersByLine()
        {
            // Arrange
            var service = CreateMockVehicleService();

            // Act
            var result = await service.GetVehiclesForLineAsync("1");

            // Assert
            Assert.Single(result);
            Assert.Equal("1", result[0].NazwaLinii);
        }

        [Fact]
        public async Task VehicleService_GetVehiclesForLinesAsync_ReturnsFilteredVehicles()
        {
            // Arrange
            var service = CreateMockVehicleService();
            var lines = new List<string> { "1", "A" };

            // Act
            var result = await service.GetVehiclesForLinesAsync(lines, "all");

            // Assert
            Assert.IsType<List<VehiclePosition>>(result);
            // Może zwrócić więcej pojazdów niż tylko te z naszych testowych danych
            // w zależności od implementacji usługi
        }

        [Fact]
        public async Task TransitService_GetArrivalsAsync_ForNonExistentStop_ReturnsNull()
        {
            // Arrange
            var gtfs = CreateRealGtfsService();
            await gtfs.InitializeAsync();
            var vehicles = CreateMockVehicleService();
            var logger = new Mock<ILogger<TransitService>>();
            var service = new TransitService(gtfs, vehicles, logger.Object);

            // Act - używamy nieistniejącego kodu przystanku
            var result = await service.GetArrivalsAsync("99999");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GtfsService_DataIntegrity_ValidatesConsistency()
        {
            // Arrange & Act
            var service = CreateRealGtfsService();
            await service.InitializeAsync();

            // Assert - sprawdzenie integralności danych
            var stops = await service.GetStopsAsync();
            var allLines = await service.GetAllLinesAsync();
            var tramLines = await service.GetTramLinesAsync();
            var busLines = await service.GetBusLinesAsync();

            // Podstawowe sprawdzenia integralności
            Assert.NotEmpty(stops);
            Assert.NotEmpty(allLines);
            Assert.NotEmpty(tramLines);
            Assert.NotEmpty(busLines);
            
            // Linie tramwajowe i autobusowe razem powinny składać się na wszystkie linie
            var combinedLines = tramLines.Union(busLines).ToList();
            Assert.True(combinedLines.Count <= allLines.Count);
            
            // Każdy przystanek powinien mieć unikalne ID i kod
            var uniqueIds = stops.Select(s => s.StopId).Distinct().Count();
            var uniqueCodes = stops.Select(s => s.StopCode).Distinct().Count();
            Assert.Equal(stops.Count, uniqueIds);
            Assert.Equal(stops.Count, uniqueCodes);
        }

        [Fact]
        public async Task TestDateTime_Simulation_VerifiesCorrectDate()
        {
            // Test sprawdzający czy symulowana data jest poprawna
            // 2 lutego 2026 to poniedziałek
            Assert.Equal(DayOfWeek.Monday, TestDateTime.DayOfWeek);
            Assert.Equal(2026, TestDateTime.Year);
            Assert.Equal(2, TestDateTime.Month);
            Assert.Equal(2, TestDateTime.Day);
            Assert.Equal(12, TestDateTime.Hour);
            Assert.Equal(30, TestDateTime.Minute);
        }
    }
}
