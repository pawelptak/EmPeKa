using BenchmarkDotNet.Attributes;
using EmPeKa.Controllers;
using EmPeKa.Services;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using System;

[IterationCount(20)]
public class StopsControllerBenchmarks
{
    private StopsController _controller;
    private IGtfsService _gtfsService;
    private ITransitService _transitService;
    private ILogger<StopsController> _logger;

    [GlobalSetup]
    public void Setup()
    {
        _logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<StopsController>.Instance;
        var gtfsLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<GtfsService>.Instance;
        var transitLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<TransitService>.Instance;
        var vehicleLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<VehicleService>.Instance;

        var httpClient = new System.Net.Http.HttpClient();
        var memoryCache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());

        var configuration = new ConfigurationBuilder().AddJsonFile("appsettings.json", optional: true).Build();
        _gtfsService = new GtfsService(gtfsLogger, httpClient, configuration);
        var vehicleService = new VehicleService(httpClient, memoryCache, vehicleLogger, _gtfsService);
        _transitService = new TransitService(_gtfsService, vehicleService, transitLogger);
        _controller = new StopsController(_gtfsService, _transitService, _logger);

        _gtfsService.InitializeAsync().GetAwaiter().GetResult();
        Console.WriteLine($"Loaded stops: {_gtfsService.GetStopsAsync().Result.Count}");
    }

    [Benchmark]
    public async Task GetArrivals_Benchmark()
    {
        await _controller.GetArrivals("10605", 3);
    }
}