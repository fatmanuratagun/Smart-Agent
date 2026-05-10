using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SmartAgent.Models;
using SmartAgent.Services;

namespace SmartAgent.Controllers
{
    public class HomeController : Controller
    {
        private readonly SmartAgentService _agentService;

        public HomeController(SmartAgentService agentService)
        {
            _agentService = agentService;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Index(string userQuery)
        {
            if (string.IsNullOrEmpty(userQuery))
            {
                ViewBag.Message = "Lütfen bir ürün veya bütçe girin.";
                return View();
            }

            // Kullanýcýnýn sorusunu ajana gönderiyoruz
            string aiResponse = await _agentService.GetShoppingAdviceAsync(userQuery);

            ViewBag.UserQuery = userQuery;
            ViewBag.Message = aiResponse;

            return View();
        }
    }
}
