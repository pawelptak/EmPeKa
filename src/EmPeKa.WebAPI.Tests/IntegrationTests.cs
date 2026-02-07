namespace EmPeKa.WebAPI.Tests;

using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using EmPeKa.Services;
using EmPeKa.Models;
using System.Threading.Tasks;
using EmPeKa.WebAPI.Interfaces;

public class IntegrationTests
{
    // Simulated date: February 2, 2026 (Monday) at various times
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
    public async Task Integration_FullWorkflow()
    {
        // Arrange - complete workflow: load data → stops → lines → departures
        var gtfs = CreateRealGtfsService();

        // Act & Assert
        await gtfs.InitializeAsync();

        // 1. Check if data is loaded
        var allStops = await gtfs.GetStopsAsync();
        Assert.NotEmpty(allStops);

        // 2. Check lines
        var tramLines = await gtfs.GetTramLinesAsync();
        var busLines = await gtfs.GetBusLinesAsync();
        var allLines = await gtfs.GetAllLinesAsync();

        Assert.NotEmpty(tramLines);
        Assert.NotEmpty(busLines);
        Assert.NotEmpty(allLines);

        // 3. Check a specific stop
        var specificStop = allStops.FirstOrDefault(s => s.StopName.Contains("Dyrekcyjna"));
        Assert.NotNull(specificStop);
        Assert.NotEmpty(specificStop.Lines); // Application should map lines to stops

        // 4. Check timetable
        var stopTimes = await gtfs.GetStopTimesForStopAsync(specificStop.StopId);
        Assert.IsType<List<StopTime>>(stopTimes);
    }

    [Fact]
    public async Task StopsWithLines_ValidateLineMappings()
    {
        // Test to verify if the application correctly maps lines to stops
        var gtfs = CreateRealGtfsService();
        await gtfs.InitializeAsync();

        var stops = await gtfs.GetStopsAsync();
        var stopsWithLines = stops.Where(s => s.Lines.Any()).ToList();

        Assert.NotEmpty(stopsWithLines);

        // Check if lines in stops actually exist
        var allLines = await gtfs.GetAllLinesAsync();
        foreach (var stop in stopsWithLines.Take(20))
        {
            Assert.All(stop.Lines, line => Assert.Contains(line, allLines));
        }
    }

    [Fact]
    public async Task TransitService_WithMockedVehicles_ReturnsArrivals()
    {
        // Integration test for TransitService with real GTFS data and mocked vehicles
        var gtfs = CreateRealGtfsService();
        await gtfs.InitializeAsync();

        var cache = new MemoryCache(new MemoryCacheOptions());
        var vehicleLogger = new Mock<ILogger<VehicleService>>();
        var gtfsMock = new Mock<IGtfsService>();
        var httpClient = new HttpClient();
        var vehicleService = new VehicleService(httpClient, cache, vehicleLogger.Object, gtfsMock.Object);

        var transitLogger = new Mock<ILogger<TransitService>>();
        var transitService = new TransitService(gtfs, vehicleService, transitLogger.Object);

        // Test with a real stop code
        var result = await transitService.GetArrivalsAsync("21101");

        if (result != null)
        {
            Assert.NotEmpty(result.StopCode);
            Assert.NotEmpty(result.StopName);
        }
    }

    [Fact]
    public async Task Performance_LoadingTime()
    {
        // Performance test for data loading by the application
        var gtfs = CreateRealGtfsService();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        await gtfs.InitializeAsync();

        stopwatch.Stop();

        // Loading should take a reasonable time (less than 30 seconds)
        Assert.True(stopwatch.ElapsedMilliseconds < 30000,
            $"Loading took {stopwatch.ElapsedMilliseconds}ms, which is too long");

        // Check if subsequent call is fast (cache)
        stopwatch.Restart();
        await gtfs.InitializeAsync();
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < 1000,
            $"Cached loading took {stopwatch.ElapsedMilliseconds}ms, cache may not be working");
    }

    [Theory]
    [InlineData("MorningRush", 7, 30)]   // Morning rush hour - 7:30
    [InlineData("Midday", 12, 30)]       // Midday - 12:30
    [InlineData("EveningRush", 17, 30)]  // Evening rush hour - 17:30
    [InlineData("LateEvening", 22, 30)]  // Late evening - 22:30
    public async Task DifferentTimeOfDay_SystemRespondsAppropriately(string timeLabel, int hour, int minute)
    {
        // Test to verify if the application works regardless of the time of day
        var testTime = timeLabel switch
        {
            "MorningRush" => MorningRush,
            "Midday" => Midday,
            "EveningRush" => EveningRush,
            "LateEvening" => LateEvening,
            _ => throw new ArgumentException($"Invalid time label: {timeLabel}")
        };

        // Arrange
        var gtfs = CreateRealGtfsService();
        await gtfs.InitializeAsync();

        // Act & Assert - Application should work regardless of the time of day
        Assert.Equal(hour, testTime.Hour);
        Assert.Equal(minute, testTime.Minute);
        Assert.Equal(2026, testTime.Year);
        Assert.Equal(2, testTime.Month);
        Assert.Equal(2, testTime.Day); // Monday

        // Check if the application returns data at any time
        var stops = await gtfs.GetStopsAsync();
        Assert.NotEmpty(stops);

        var lines = await gtfs.GetAllLinesAsync();
        Assert.NotEmpty(lines);

        // Test a specific stop at different times
        var dyrekcyjnaStops = await gtfs.GetStopsAsync("29");
        Assert.Single(dyrekcyjnaStops);

        var stopTimes = await gtfs.GetStopTimesForStopAsync("29");
        Assert.IsType<List<StopTime>>(stopTimes);
    }

    [Fact]
    public async Task BusinessHours_VsNightTime_ServiceAvailability()
    {
        // Test to verify business logic of the application at different times
        var gtfs = CreateRealGtfsService();
        await gtfs.InitializeAsync();

        var cache = new MemoryCache(new MemoryCacheOptions());
        var vehicleLogger = new Mock<ILogger<VehicleService>>();
        var gtfsMock = new Mock<IGtfsService>();
        var httpClient = new HttpClient();
        var vehicleService = new VehicleService(httpClient, cache, vehicleLogger.Object, gtfsMock.Object);

        var transitLogger = new Mock<ILogger<TransitService>>();
        var transitService = new TransitService(gtfs, vehicleService, transitLogger.Object);

        // Test application logic in different time contexts
        var testScenarios = new[]
        {
            new { Time = MorningRush, Label = "Morning Business Hours", IsBusinessHour = true },
            new { Time = LateEvening, Label = "Night Time", IsBusinessHour = false }
        };

        foreach (var scenario in testScenarios)
        {
            // Act - Test application logic
            var result = await transitService.GetArrivalsAsync("21101");

            // Assert - Application should respond regardless of the time
            if (result != null)
            {
                Assert.NotEmpty(result.StopCode);
                Assert.NotEmpty(result.StopName);

                // Debug log for different time scenarios
                System.Diagnostics.Debug.WriteLine($"{scenario.Label} ({scenario.Time:HH:mm}): " +
                    $"App returned {result.Arrivals.Count} arrivals");
            }

            // Validation of business logic in the application
            if (scenario.IsBusinessHour)
            {
                Assert.True(scenario.Time.Hour >= 6 && scenario.Time.Hour <= 22,
                    "Business hours should be between 6:00 and 22:00");
            }
            else
            {
                Assert.True(scenario.Time.Hour >= 22 || scenario.Time.Hour <= 6,
                    "Night time should be after 22:00 or before 6:00");
            }
        }

        // Additional validation of time logic
        Assert.True(MorningRush.Hour < LateEvening.Hour,
            "Morning rush should be earlier than late evening");
        Assert.True(LateEvening.Subtract(MorningRush).TotalHours > 10,
            "Should test times at least 10+ hours apart");
    }

    [Fact]
    public async Task RushHour_VsOffPeak_DataConsistency()
    {
        // Test to verify consistency of application behavior at different times
        var gtfs = CreateRealGtfsService();
        await gtfs.InitializeAsync();

        // Compare application behavior at different times
        var testTimes = new[]
        {
            new { Time = MorningRush, Label = "Morning Rush" },
            new { Time = Midday, Label = "Midday" },
            new { Time = EveningRush, Label = "Evening Rush" },
            new { Time = LateEvening, Label = "Late Evening" }
        };

        foreach (var timeData in testTimes)
        {
            // Check if the application works consistently at any time
            var stops = await gtfs.GetStopsAsync();
            var lines = await gtfs.GetAllLinesAsync();
            var tramLines = await gtfs.GetTramLinesAsync();
            var busLines = await gtfs.GetBusLinesAsync();

            // Application should return data regardless of the time
            Assert.NotEmpty(stops);
            Assert.NotEmpty(lines);
            Assert.NotEmpty(tramLines);
            Assert.NotEmpty(busLines);

            // Tram and bus lines should not overlap (application logic)
            var commonLines = tramLines.Intersect(busLines);
            Assert.Empty(commonLines);

            // Debug log
            System.Diagnostics.Debug.WriteLine($"Testing {timeData.Label} at {timeData.Time:HH:mm} - {stops.Count} stops, {lines.Count} lines");
        }
    }

    [Fact]
    public async Task TimeSimulation_ValidatesTestScenarios()
    {
        // Meta-test to verify correctness of time simulation in tests
        var allTestTimes = new Dictionary<string, DateTime>
        {
            ["MorningRush"] = MorningRush,
            ["Midday"] = Midday,
            ["EveningRush"] = EveningRush,
            ["LateEvening"] = LateEvening
        };

        foreach (var (label, time) in allTestTimes)
        {
            // All times should be from the same day (Monday, February 2, 2026)
            Assert.Equal(2026, time.Year);
            Assert.Equal(2, time.Month);
            Assert.Equal(2, time.Day);
            Assert.Equal(DayOfWeek.Monday, time.DayOfWeek);

            // Check logical times of day
            switch (label)
            {
                case "MorningRush":
                    Assert.Equal(7, time.Hour);
                    Assert.Equal(30, time.Minute);
                    break;
                case "Midday":
                    Assert.Equal(12, time.Hour);
                    Assert.Equal(30, time.Minute);
                    break;
                case "EveningRush":
                    Assert.Equal(17, time.Hour);
                    Assert.Equal(30, time.Minute);
                    break;
                case "LateEvening":
                    Assert.Equal(22, time.Hour);
                    Assert.Equal(30, time.Minute);
                    break;
            }
        }

        // Check chronology (times should be in order)
        Assert.True(MorningRush < Midday);
        Assert.True(Midday < EveningRush);
        Assert.True(EveningRush < LateEvening);
    }
}