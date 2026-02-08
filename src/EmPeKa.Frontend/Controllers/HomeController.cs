using EmPeKa.Frontend.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace EmPeKa.Frontend.Controllers
{
    public class HomeController : Controller
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<HomeController> _logger;

        public HomeController(IHttpClientFactory httpClientFactory, ILogger<HomeController> logger)
        {
            _httpClient = httpClientFactory.CreateClient("EmPeKaApi");
            _logger = logger;
        }

        public async Task<IActionResult> Index(int? count)
        {
            var stopCodes = Request.Query["stopCode"].ToList();

            if (stopCodes.Count == 0)
            {
                stopCodes.Add("10606"); // Default example stop
            }

            if (stopCodes.Count == 1)
            {
                var stopCode = stopCodes.Single();
                ArrivalsResponse? model = null;
                try
                {
                    string url = $"stops/{stopCode}/arrivals";
                    if (count.HasValue)
                    {
                        url += $"?count={count.Value}";
                    }
                    model = await _httpClient.GetFromJsonAsync<ArrivalsResponse>(url);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to fetch arrivals for stop {StopCode}", stopCode);
                    ModelState.AddModelError(string.Empty, "Nie uda³o siê pobraæ danych z API.");
                }

                return View("Index", model ?? new ArrivalsResponse { StopCode = stopCode ?? string.Empty, StopName = "Brak danych", Arrivals = [] });
            }

            List<ArrivalItem>? batchArrivals = null;

            try
            {
                var batchRequest = new ArrivalsBatchRequest
                {
                    StopCodes = stopCodes,
                    CountPerStop = count ?? 3
                };

                var response = await _httpClient.PostAsJsonAsync("stops/arrivals/batch", batchRequest);
                response.EnsureSuccessStatusCode();
                batchArrivals = await response.Content.ReadFromJsonAsync<List<ArrivalItem>>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch batch arrivals for stops {StopCodes}", string.Join(",", stopCodes));
                ModelState.AddModelError(string.Empty, "Nie uda³o siê pobraæ danych z API.");
            }

            return View("BatchIndex", batchArrivals ?? new List<ArrivalItem>());
        }

        [HttpGet("api/arrivals/{stopCode}")]
        public async Task<IActionResult> GetArrivalsJson(string stopCode, int? count)
        {
            try
            {
                string url = $"stops/{stopCode}/arrivals";
                if (count.HasValue)
                {
                    url += $"?count={count.Value}";
                }
                var model = await _httpClient.GetFromJsonAsync<ArrivalsResponse>(url);
                return Json(model ?? new ArrivalsResponse { StopCode = stopCode, StopName = "Brak danych", Arrivals = [] });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch arrivals for stop {StopCode}", stopCode);
                return StatusCode(500, new { error = "Nie uda³o siê pobraæ danych z API." });
            }
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
