using Microsoft.AspNetCore.Mvc;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using EmPeka.Frontend.Models;
using System.Text.Json;

namespace EmPeka.Frontend.Controllers
{
    public class StopsController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        public StopsController(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<IActionResult> Index()
        {
            var client = _httpClientFactory.CreateClient("EmPeKaApi");
            var response = await client.GetAsync("stops");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var stopsResponse = JsonSerializer.Deserialize<StopsResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return View(stopsResponse?.Stops ?? new List<StopModel>());
        }
    }
}