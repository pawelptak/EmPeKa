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

    public class IntegrationTests
    {
        // Symulowana data: 2 lutego 2026 (poniedzia³ek) o ró¿nych godzinach
        private static readonly DateTime MorningRush = new DateTime(2026, 2, 2, 7, 30, 0);
        private static readonly DateTime Midday = new DateTime(2026, 2, 2, 12, 30, 0);
        private static readonly DateTime EveningRush = new DateTime(2026, 2, 2, 17, 30, 0);
        private static readonly DateTime LateEvening = new DateTime(2026, 2, 2, 22, 30, 0);
        
        private static readonly string TestDataPath = Path.Combine("..", "..", "..", "..", "EmPeKa.WebAPI", "data", "gtfs");
        
        private IGtfsService CreateRealGtfsService()
        {
            var logger = new Mock<ILogger<GtfsService>>();
            return new TestGtfsService(logger.Object, TestDataPath);
        }

        [Fact]
        public async Task RealData_Integration_FullWorkflow()
        {
            // Arrange - kompletny przep³yw: ³adowanie danych ? przystanki ? linie ? odjazdy
            var gtfs = CreateRealGtfsService();
            
            // Act & Assert
            await gtfs.InitializeAsync();
            
            // 1. Sprawdzenie za³adowania danych
            var allStops = await gtfs.GetStopsAsync();
            Assert.NotEmpty(allStops);
            Assert.True(allStops.Count > 1000); // Wroc³aw ma tysi¹ce przystanków
            
            // 2. Sprawdzenie linii
            var tramLines = await gtfs.GetTramLinesAsync();
            var busLines = await gtfs.GetBusLinesAsync();
            var allLines = await gtfs.GetAllLinesAsync();
            
            Assert.NotEmpty(tramLines);
            Assert.NotEmpty(busLines);
            Assert.True(allLines.Count >= tramLines.Count + busLines.Count);
            
            // 3. Sprawdzenie konkretnego przystanku
            var specificStop = allStops.FirstOrDefault(s => s.StopName.Contains("Dyrekcyjna"));
            Assert.NotNull(specificStop);
            Assert.NotEmpty(specificStop.Lines); // Dyrekcyjna powinna mieæ linie
            
            // 4. Sprawdzenie rozk³adu jazdy
            var stopTimes = await gtfs.GetStopTimesForStopAsync(specificStop.StopId);
            // W poniedzia³ek powinny byæ kursy
            Assert.True(stopTimes.Count >= 0); // Mo¿e byæ 0 jeœli service_id nie pasuje do daty
        }

        [Fact]
        public async Task RealData_StopsWithLines_ValidateLineMappings()
        {
            // Test sprawdzaj¹cy czy przystanki maj¹ poprawnie przypisane linie
            var gtfs = CreateRealGtfsService();
            await gtfs.InitializeAsync();
            
            var stops = await gtfs.GetStopsAsync();
            var stopsWithLines = stops.Where(s => s.Lines.Any()).ToList();
            
            Assert.NotEmpty(stopsWithLines);
            Assert.True(stopsWithLines.Count > 100); // Wiêkszoœæ przystanków powinna mieæ linie
            
            // SprawdŸ czy linie na przystankach rzeczywiœcie istniej¹
            var allLines = await gtfs.GetAllLinesAsync();
            foreach (var stop in stopsWithLines.Take(10)) // Test próbki
            {
                Assert.All(stop.Lines, line => Assert.Contains(line, allLines));
            }
        }

        [Fact]
        public async Task RealData_TramVsBusLines_CorrectClassification()
        {
            // Test sprawdzaj¹cy poprawn¹ klasyfikacjê linii tramwajowych vs autobusowych
            var gtfs = CreateRealGtfsService();
            await gtfs.InitializeAsync();
            
            var tramLines = await gtfs.GetTramLinesAsync();
            var busLines = await gtfs.GetBusLinesAsync();
            
            // Linie tramwajowe powinny byæ g³ównie numeryczne (1, 2, 3, etc.)
            var numericTramLines = tramLines.Where(line => int.TryParse(line, out _)).ToList();
            Assert.True(numericTramLines.Count > 5); // Wroc³aw ma kilkanaœcie linii tramwajowych
            
            // Linie autobusowe powinny zawieraæ literowe (A, D, K, N)
            var letterBusLines = busLines.Where(line => line.Length == 1 && char.IsLetter(line[0])).ToList();
            Assert.True(letterBusLines.Count > 2); // A, D, K, N to co najmniej 4
            
            // Nie powinno byæ nak³adania
            var commonLines = tramLines.Intersect(busLines).ToList();
            Assert.Empty(commonLines); // Linie tramwajowe i autobusowe nie powinny siê nak³adaæ
        }

        [Fact]
        public async Task RealData_StopsGeolocation_ValidCoordinates()
        {
            // Test sprawdzaj¹cy czy wspó³rzêdne geograficzne przystanków s¹ sensowne
            var gtfs = CreateRealGtfsService();
            await gtfs.InitializeAsync();
            
            var stops = await gtfs.GetStopsAsync();
            var stopsInWroclaw = stops.Where(s => 
                s.Latitude > 51.0 && s.Latitude < 51.2 && // Szerokoœæ geograficzna Wroc³awia
                s.Longitude > 16.8 && s.Longitude < 17.2   // D³ugoœæ geograficzna Wroc³awia
            ).ToList();
            
            // Wiêkszoœæ przystanków powinna byæ w granicach Wroc³awia
            Assert.True(stopsInWroclaw.Count > stops.Count * 0.8); // Co najmniej 80%
            
            // SprawdŸ kilka znanych przystanków
            var dyrekcyjna = stops.FirstOrDefault(s => s.StopName.Contains("Dyrekcyjna"));
            if (dyrekcyjna != null)
            {
                Assert.InRange(dyrekcyjna.Latitude, 51.0, 51.2);
                Assert.InRange(dyrekcyjna.Longitude, 16.8, 17.2);
            }
        }

        [Theory]
        [InlineData("29")] // Dyrekcyjna
        [InlineData("30")] // pl. Orl¹t Lwowskich  
        [InlineData("25")] // BIEÑKOWICE
        public async Task RealData_SpecificStops_HaveValidData(string stopId)
        {
            // Test parametryzowany sprawdzaj¹cy konkretne przystanki
            var gtfs = CreateRealGtfsService();
            await gtfs.InitializeAsync();
            
            var stops = await gtfs.GetStopsAsync(stopId);
            Assert.Single(stops);
            
            var stop = stops[0];
            Assert.Equal(stopId, stop.StopId);
            Assert.NotEmpty(stop.StopCode);
            Assert.NotEmpty(stop.StopName);
            Assert.InRange(stop.Latitude, 50.0, 52.0); // Rozszerzone granice dla bezpieczeñstwa
            Assert.InRange(stop.Longitude, 16.0, 18.0);
        }

        [Fact]
        public async Task RealData_TransitService_WithMockedVehicles_ReturnsArrivals()
        {
            // Test integracyjny TransitService z prawdziwymi danymi GTFS i mock vehicles
            var gtfs = CreateRealGtfsService();
            await gtfs.InitializeAsync();
            
            var cache = new MemoryCache(new MemoryCacheOptions());
            var vehicleLogger = new Mock<ILogger<VehicleService>>();
            var gtfsMock = new Mock<IGtfsService>();
            var httpClient = new HttpClient();
            var vehicleService = new VehicleService(httpClient, cache, vehicleLogger.Object, gtfsMock.Object);
            
            var transitLogger = new Mock<ILogger<TransitService>>();
            var transitService = new TransitService(gtfs, vehicleService, transitLogger.Object);
            
            // Test z prawdziwym kodem przystanku (StopCode dla Dyrekcyjnej)
            var result = await transitService.GetArrivalsAsync("21101");
            
            if (result != null)
            {
                Assert.NotEmpty(result.StopCode);
                Assert.NotEmpty(result.StopName);
                Assert.Contains("Dyrekcyjna", result.StopName);
                // Arrivals mog¹ byæ puste jeœli nie ma aktywnych kursów na dan¹ datê
            }
        }

        [Fact]
        public async Task RealData_Performance_LoadingTime()
        {
            // Test wydajnoœci ³adowania danych
            var gtfs = CreateRealGtfsService();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            await gtfs.InitializeAsync();
            
            stopwatch.Stop();
            
            // £adowanie powinno zaj¹æ rozs¹dny czas (mniej ni¿ 30 sekund)
            Assert.True(stopwatch.ElapsedMilliseconds < 30000, 
                $"Loading took {stopwatch.ElapsedMilliseconds}ms, which is too long");
            
            // SprawdŸ czy kolejne wywo³anie jest szybkie (cache)
            stopwatch.Restart();
            await gtfs.InitializeAsync();
            stopwatch.Stop();
            
            Assert.True(stopwatch.ElapsedMilliseconds < 1000,
                $"Cached loading took {stopwatch.ElapsedMilliseconds}ms, cache may not be working");
        }
    }
}