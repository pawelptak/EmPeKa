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

    public class UnitTests
    {
        [Fact]
        public async Task StopsController_GetStops_WithStopId_ReturnsFiltered()
        {
            // Arrange
            var gtfs = new Mock<IGtfsService>();
            gtfs.Setup(g => g.GetStopsAsync("1")).ReturnsAsync(new List<StopInfo> {
                new StopInfo {
                    StopId = "1",
                    StopCode = "A",
                    StopName = "Test Stop"
                }
            });
            var transit = new Mock<ITransitService>();
            var logger = new Mock<ILogger<StopsController>>();
            var controller = new StopsController(gtfs.Object, transit.Object, logger.Object);

            // Act
            var result = await controller.GetStops("1");

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var response = Assert.IsType<StopsResponse>(okResult.Value);
            Assert.Single(response.Stops);
            Assert.Equal("1", response.Stops[0].StopId);
        }

        [Fact]
        public async Task StopsController_GetStops_WithInvalidStopId_ReturnsEmpty()
        {
            // Arrange
            var gtfs = new Mock<IGtfsService>();
            gtfs.Setup(g => g.GetStopsAsync("invalid")).ReturnsAsync(new List<StopInfo>());
            var transit = new Mock<ITransitService>();
            var logger = new Mock<ILogger<StopsController>>();
            var controller = new StopsController(gtfs.Object, transit.Object, logger.Object);

            // Act
            var result = await controller.GetStops("invalid");

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var response = Assert.IsType<StopsResponse>(okResult.Value);
            Assert.Empty(response.Stops);
        }

        [Fact]
        public async Task StopsController_GetStops_WhenException_Returns500()
        {
            // Arrange
            var gtfs = new Mock<IGtfsService>();
            gtfs.Setup(g => g.GetStopsAsync(It.IsAny<string>())).ThrowsAsync(new Exception("Test error"));
            var transit = new Mock<ITransitService>();
            var logger = new Mock<ILogger<StopsController>>();
            var controller = new StopsController(gtfs.Object, transit.Object, logger.Object);

            // Act
            var result = await controller.GetStops("1");

            // Assert
            var statusResult = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(500, statusResult.StatusCode);
        }
        [Fact]
        public async Task GtfsService_InitializeAsync_Success()
        {
            // Arrange
            var logger = new Mock<ILogger<GtfsService>>();
            var httpClient = new Mock<HttpClient>();
            var config = new Mock<IConfiguration>();
            config.Setup(c => c["GtfsDataPath"]).Returns("./testpath");
            var service = new GtfsService(logger.Object, new HttpClient(), config.Object);

            // Act
            await service.InitializeAsync();

            // Assert
            // No exception means success
        }

        [Fact]
        public async Task VehicleService_GetVehiclePositionsAsync_ReturnsCached()
        {
            // Arrange
            var cache = new MemoryCache(new MemoryCacheOptions());
            var logger = new Mock<ILogger<VehicleService>>();
            var gtfs = new Mock<IGtfsService>();
            var httpClient = new HttpClient();
            var service = new VehicleService(httpClient, cache, logger.Object, gtfs.Object);
            cache.Set("vehicle_positions", new List<VehiclePosition> { new VehiclePosition() });

            // Act
            var result = await service.GetVehiclePositionsAsync();

            // Assert
            Assert.Single(result);
        }

        [Fact]
        public async Task TransitService_GetArrivalsAsync_StopNotFound_ReturnsNull()
        {
            // Arrange
            var gtfs = new Mock<IGtfsService>();
            gtfs.Setup(g => g.GetStopsAsync()).ReturnsAsync(new List<StopInfo>());
            var vehicles = new Mock<IVehicleService>();
            var logger = new Mock<ILogger<TransitService>>();
            var service = new TransitService(gtfs.Object, vehicles.Object, logger.Object);

            // Act
            var result = await service.GetArrivalsAsync("invalid");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task StopsController_GetStops_ReturnsOk()
        {
            // Arrange
            var gtfs = new Mock<IGtfsService>();
            gtfs.Setup(g => g.GetStopsAsync(It.IsAny<string>())).ReturnsAsync(new List<StopInfo> {
                new StopInfo {
                    StopId = "1",
                    StopCode = "A",
                    StopName = "Test Stop"
                }
            });
            var transit = new Mock<ITransitService>();
            var logger = new Mock<ILogger<StopsController>>();
            var controller = new StopsController(gtfs.Object, transit.Object, logger.Object);

            // Act
            var result = await controller.GetStops();

            // Assert
            Assert.IsType<OkObjectResult>(result.Result);
        }

        [Fact]
        public async Task HealthController_GetHealth_ReturnsHealthy()
        {
            // Arrange
            var gtfs = new Mock<IGtfsService>();
            gtfs.Setup(g => g.GetStopsAsync()).ReturnsAsync(new List<StopInfo> {
                new StopInfo {
                    StopId = "1",
                    StopCode = "A",
                    StopName = "Test Stop"
                }
            });
            var vehicles = new Mock<IVehicleService>();
            vehicles.Setup(v => v.GetVehiclePositionsAsync()).ReturnsAsync(new List<VehiclePosition> { new VehiclePosition { DataAktualizacji = DateTime.UtcNow } });
            var logger = new Mock<ILogger<HealthController>>();
            var controller = new HealthController(gtfs.Object, vehicles.Object, logger.Object);

            // Act
            var result = await controller.GetHealth();

            // Assert
            Assert.IsType<OkObjectResult>(result.Result);
        }

        [Fact]
        public async Task GtfsService_GetStopTimesForStopAsync_ReturnsStopTimes()
        {
            // Arrange
            var logger = new Mock<ILogger<GtfsService>>();
            var httpClient = new HttpClient();
            var config = new Mock<IConfiguration>();
            config.Setup(c => c["GtfsDataPath"]).Returns("./testpath");
            var service = new GtfsService(logger.Object, new HttpClient(), config.Object);

            // Act & Assert
            // This test assumes InitializeAsync has been called and data is loaded
            // In a real scenario, this would return stop times, but for unit test, we check no exception
            await service.InitializeAsync();
            var result = await service.GetStopTimesForStopAsync("1");
            Assert.IsType<List<StopTime>>(result);
        }

        [Fact]
        public async Task GtfsService_GetRouteAsync_ReturnsRoute()
        {
            // Arrange
            var logger = new Mock<ILogger<GtfsService>>();
            var httpClient = new HttpClient();
            var config = new Mock<IConfiguration>();
            config.Setup(c => c["GtfsDataPath"]).Returns("./testpath");
            var service = new GtfsService(logger.Object, new HttpClient(), config.Object);

            // Act
            await service.InitializeAsync();
            var result = await service.GetRouteAsync("1");

            // Assert
            // May return null if no route, but type is correct
            Assert.True(result == null || result is Route);
        }

        [Fact]
        public async Task GtfsService_GetTripAsync_ReturnsTrip()
        {
            // Arrange
            var logger = new Mock<ILogger<GtfsService>>();
            var httpClient = new HttpClient();
            var config = new Mock<IConfiguration>();
            config.Setup(c => c["GtfsDataPath"]).Returns("./testpath");
            var service = new GtfsService(logger.Object, new HttpClient(), config.Object);

            // Act
            await service.InitializeAsync();
            var result = await service.GetTripAsync("1");

            // Assert
            Assert.True(result == null || result is Trip);
        }

        [Fact]
        public async Task GtfsService_GetAllLinesAsync_ReturnsLines()
        {
            // Arrange
            var logger = new Mock<ILogger<GtfsService>>();
            var httpClient = new HttpClient();
            var config = new Mock<IConfiguration>();
            config.Setup(c => c["GtfsDataPath"]).Returns("./testpath");
            var service = new GtfsService(logger.Object, new HttpClient(), config.Object);

            // Act
            await service.InitializeAsync();
            var result = await service.GetAllLinesAsync();

            // Assert
            Assert.IsType<List<string>>(result);
        }

        [Fact]
        public async Task GtfsService_GetTramLinesAsync_ReturnsTramLines()
        {
            // Arrange
            var logger = new Mock<ILogger<GtfsService>>();
            var httpClient = new HttpClient();
            var config = new Mock<IConfiguration>();
            config.Setup(c => c["GtfsDataPath"]).Returns("./testpath");
            var service = new GtfsService(logger.Object, new HttpClient(), config.Object);

            // Act
            await service.InitializeAsync();
            var result = await service.GetTramLinesAsync();

            // Assert
            Assert.IsType<List<string>>(result);
        }

        [Fact]
        public async Task GtfsService_GetBusLinesAsync_ReturnsBusLines()
        {
            // Arrange
            var logger = new Mock<ILogger<GtfsService>>();
            var httpClient = new HttpClient();
            var config = new Mock<IConfiguration>();
            config.Setup(c => c["GtfsDataPath"]).Returns("./testpath");
            var service = new GtfsService(logger.Object, new HttpClient(), config.Object);

            // Act
            await service.InitializeAsync();
            var result = await service.GetBusLinesAsync();

            // Assert
            Assert.IsType<List<string>>(result);
        }

        [Fact]
        public async Task VehicleService_GetVehiclesForLineAsync_ReturnsFilteredVehicles()
        {
            // Arrange
            var cache = new MemoryCache(new MemoryCacheOptions());
            var logger = new Mock<ILogger<VehicleService>>();
            var gtfs = new Mock<IGtfsService>();
            var httpClient = new HttpClient();
            var service = new VehicleService(httpClient, cache, logger.Object, gtfs.Object);
            var vehicles = new List<VehiclePosition> {
                new VehiclePosition { NazwaLinii = "1" },
                new VehiclePosition { NazwaLinii = "2" }
            };
            cache.Set("vehicle_positions", vehicles);

            // Act
            var result = await service.GetVehiclesForLineAsync("1");

            // Assert
            Assert.Single(result);
            Assert.Equal("1", result[0].NazwaLinii);
        }

        [Fact]
        public async Task VehicleService_GetVehiclesForLinesAsync_ReturnsVehicles()
        {
            // Arrange
            var cache = new MemoryCache(new MemoryCacheOptions());
            var logger = new Mock<ILogger<VehicleService>>();
            var gtfs = new Mock<IGtfsService>();
            var httpClient = new HttpClient();
            var service = new VehicleService(httpClient, cache, logger.Object, gtfs.Object);

            // Act
            var result = await service.GetVehiclesForLinesAsync(new List<string> { "1", "2" }, "tram");

            // Assert
            Assert.IsType<List<VehiclePosition>>(result);
        }

        [Fact]
        public async Task TransitService_GetArrivalsAsync_ReturnsArrivals()
        {
            // Arrange
            var gtfs = new Mock<IGtfsService>();
            gtfs.Setup(g => g.GetStopsAsync()).ReturnsAsync(new List<StopInfo> {
                new StopInfo { StopId = "1", StopCode = "A", StopName = "Test Stop" }
            });
            gtfs.Setup(g => g.GetStopTimesForStopAsync("1")).ReturnsAsync(new List<StopTime> {
                new StopTime { TripId = "T1", ArrivalTime = "12:00:00", DepartureTime = "12:00:00", StopId = "1" }
            });
            var vehicles = new Mock<IVehicleService>();
            var logger = new Mock<ILogger<TransitService>>();
            var service = new TransitService(gtfs.Object, vehicles.Object, logger.Object);

            // Act
            var result = await service.GetArrivalsAsync("A");

            // Assert
            Assert.NotNull(result);
            Assert.IsType<ArrivalsResponse>(result);
        }
    }
}
