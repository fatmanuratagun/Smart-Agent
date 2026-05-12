using Microsoft.AspNetCore.Mvc;
using SmartAgent.Services;
using System.Text;

namespace SmartAgent.Controllers
{
    public class HomeController : Controller
    {
        private readonly SmartAgentService _agentService;
        private readonly FirebaseService _firebase;

        public HomeController(SmartAgentService agentService, FirebaseService firebase)
        {
            _agentService = agentService;
            _firebase = firebase;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> History()
        {
            string userId = "user_1";
            var history = await _firebase.GetSearchHistoryAsync(userId);
            return View(history);
        }

        [HttpPost]
        public async Task<IActionResult> Index(string userQuery)
        {
            if (string.IsNullOrEmpty(userQuery))
            {
                ViewBag.Message = "Lütfen bir ürün veya bütçe girin.";
                return View();
            }

            string aiResponse = await _agentService.GetShoppingAdviceAsync(userQuery);

            string userId = "user_1";
            await _firebase.SaveSearchAsync(userId, userQuery, aiResponse.Length);
            await _firebase.SaveAdviceAsync(userId, userQuery, aiResponse);

            if (aiResponse.Contains("uyarı", StringComparison.OrdinalIgnoreCase) ||
                aiResponse.Contains("dikkat", StringComparison.OrdinalIgnoreCase) ||
                aiResponse.Contains("sahte", StringComparison.OrdinalIgnoreCase))
            {
                await _firebase.SaveWarningAsync(userId, userQuery, "fake_discount", "high");
            }

            ViewBag.UserQuery = userQuery;
            ViewBag.Message = aiResponse;
            ViewBag.FormattedMessage = FormatResponse(aiResponse);
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> DeleteSearch(string searchId)
        {
            string userId = "user_1";
            await _firebase.DeleteSearchAsync(userId, searchId);
            return RedirectToAction("History");
        }
        
        
        private string FormatResponse(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            var lines = text.Split('\n');
            var html = new StringBuilder();
            bool inTable = false;
            bool firstTableRow = true;

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();

                // Tablo satırı
                if (line.StartsWith("|"))
                {
                    if (!inTable)
                    {
                        html.AppendLine("<div style='overflow-x:auto; margin: 16px 0;'>");
                        html.AppendLine("<table style='width:100%; border-collapse:collapse; font-size:14px;'>");
                        inTable = true;
                        firstTableRow = true;
                    }

                    // Ayraç satırını atla (|---|---|)
                    if (line.Replace("|", "").Replace("-", "").Replace(" ", "") == "")
                        continue;

                    var cells = line.Split('|')
                                   .Where(c => c.Trim() != "")
                                   .ToList();

                    if (firstTableRow)
                    {
                        html.AppendLine("<thead><tr style='background:#6c63ff; color:white;'>");
                        foreach (var cell in cells)
                            html.AppendLine($"<th style='padding:10px 14px; text-align:left; font-weight:500;'>{CleanMarkdown(cell.Trim())}</th>");
                        html.AppendLine("</tr></thead><tbody>");
                        firstTableRow = false;
                    }
                    else
                    {
                        html.AppendLine("<tr style='border-bottom:1px solid #eee;'>");
                        foreach (var cell in cells)
                            html.AppendLine($"<td style='padding:9px 14px; color:#333;'>{CleanMarkdown(cell.Trim())}</td>");
                        html.AppendLine("</tr>");
                    }
                    continue;
                }

                // Tablo bitti
                if (inTable)
                {
                    html.AppendLine("</tbody></table></div>");
                    inTable = false;
                    firstTableRow = true;
                }

                // Boş satır
                if (string.IsNullOrEmpty(line))
                {
                    html.AppendLine("<br/>");
                    continue;
                }

                // Başlık satırları (emoji ile başlayanlar)
                if (line.StartsWith("🎯") || line.StartsWith("📊") || line.StartsWith("🛒") ||
                    line.StartsWith("⚠️") || line.StartsWith("🚨") || line.StartsWith("⚖️"))
                {
                    html.AppendLine($"<div style='font-size:17px; font-weight:600; color:#1a1a2e; margin:20px 0 8px;'>{CleanMarkdown(line)}</div>");
                    continue;
                }

                // Madde işaretli satırlar
                if (line.StartsWith("- ") || line.StartsWith("* "))
                {
                    html.AppendLine($"<div style='padding:3px 0 3px 16px; color:#444;'>• {CleanMarkdown(line.Substring(2))}</div>");
                    continue;
                }

                // Numaralı satırlar (1. 2. 3.)
                if (line.Length > 2 && char.IsDigit(line[0]) && line[1] == '.')
                {
                    html.AppendLine($"<div style='font-weight:600; color:#1a1a2e; margin:12px 0 4px;'>{CleanMarkdown(line)}</div>");
                    continue;
                }

                // Normal satır
                html.AppendLine($"<div style='color:#444; line-height:1.7;'>{CleanMarkdown(line)}</div>");
            }

            if (inTable)
                html.AppendLine("</tbody></table></div>");

            return html.ToString();
        }

        private string CleanMarkdown(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            // Kalın metin **text** → <strong>text</strong>
            text = System.Text.RegularExpressions.Regex.Replace(
                text, @"\*\*(.+?)\*\*", "<strong>$1</strong>");

            // İtalik *text* → <em>text</em>
            text = System.Text.RegularExpressions.Regex.Replace(
                text, @"\*(.+?)\*", "<em>$1</em>");

            // Linkler [text](url) → tıklanabilir link
            text = System.Text.RegularExpressions.Regex.Replace(
                text, @"\[(.+?)\]\((https?://[^\)]+)\)",
                "<a href='$2' target='_blank' style='color:#6c63ff; text-decoration:none; font-weight:500;'>$1 🔗</a>");

            // Düz linkler https://... → tıklanabilir
            text = System.Text.RegularExpressions.Regex.Replace(
                text, @"(?<!href=')(https?://[^\s<]+)",
                "<a href='$1' target='_blank' style='color:#6c63ff; text-decoration:none;'>$1 🔗</a>");

            return text;
        }
    }
}