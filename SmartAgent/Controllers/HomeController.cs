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

        // 🚀 SİHİRLİ METOT: Çerezi Okur veya Yeni Kimlik Üretir
        private string GetOrCreateUserId()
        {
            string cookieName = "AuraUserId";
            string userId = Request.Cookies[cookieName];

            // Eğer tarayıcıda bir kimlik yoksa (siteye ilk kez giriyorsa)
            if (string.IsNullOrEmpty(userId))
            {
                // Rastgele eşsiz bir kimlik üret (Örn: AuraUser_a1b2c3d4)
                userId = "AuraUser_" + Guid.NewGuid().ToString("N").Substring(0, 8);

                // Bu kimliği 30 gün boyunca tarayıcıda hatırla
                CookieOptions options = new CookieOptions { Expires = DateTime.Now.AddDays(30) };
                Response.Cookies.Append(cookieName, userId, options);
            }

            return userId;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> History()
        {
            // Sabit "user_1" yerine dinamik çerez kimliğini çağırıyoruz
            string userId = GetOrCreateUserId();
            var history = await _firebase.GetSearchHistoryAsync(userId);
            var advices = await _firebase.GetAdvicesAsync(userId);

            // Sorguyu cevabıyla eşleştir
            var combined = history.Select(h => new HistoryItem
            {
                Query = h.Query,
                Timestamp = h.Timestamp,
                FirebaseKey = h.FirebaseKey,
                Advice = advices.FirstOrDefault(a => a.Product == h.Query)?.Advice ?? ""
            }).ToList();

            return View(combined);
        }

        [HttpPost]
        public async Task<IActionResult> DeleteAllSearches()
        {
            string userId = GetOrCreateUserId();
            await _firebase.DeleteAllSearchesAsync(userId);
            return RedirectToAction("History");
        }

        [HttpPost]
        public async Task<IActionResult> DeleteSelectedSearches(string searchIds)
        {
            if (string.IsNullOrEmpty(searchIds))
                return RedirectToAction("History");

            string userId = GetOrCreateUserId();
            var ids = searchIds.Split(',');
            foreach (var id in ids)
            {
                await _firebase.DeleteSearchAsync(userId, id.Trim());
            }
            return RedirectToAction("History");
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
            string userId = GetOrCreateUserId(); // Dinamik kimlik

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
            ViewBag.FormattedMessage = aiResponse;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> DeleteSearch(string searchId)
        {
            string userId = GetOrCreateUserId();
            await _firebase.DeleteSearchAsync(userId, searchId);
            return RedirectToAction("History");
        }

        [HttpPost]
        public async Task<IActionResult> AjaxQuery(string userQuery)
        {
            if (string.IsNullOrEmpty(userQuery))
                return Json(new { html = "<p>Lütfen bir şey yaz.</p>" });

            string aiResponse = await _agentService.GetShoppingAdviceAsync(userQuery);
            string userId = GetOrCreateUserId(); // Dinamik kimlik

            await _firebase.SaveSearchAsync(userId, userQuery, aiResponse.Length);
            await _firebase.SaveAdviceAsync(userId, userQuery, aiResponse);

            if (aiResponse.Contains("uyarı", StringComparison.OrdinalIgnoreCase) ||
                aiResponse.Contains("dikkat", StringComparison.OrdinalIgnoreCase) ||
                aiResponse.Contains("sahte", StringComparison.OrdinalIgnoreCase))
            {
                await _firebase.SaveWarningAsync(userId, userQuery, "fake_discount", "high");
            }

            // Markdown linkleri HTML'e çevir
            string cleanedResponse = FixBrokenLinks(aiResponse);
            return Json(new { html = cleanedResponse });
        }

        private string FixBrokenLinks(string html)
        {
            if (string.IsNullOrEmpty(html)) return html;

            // Markdown [metin](url) → HTML link
            html = System.Text.RegularExpressions.Regex.Replace(
                html,
                @"\[([^\]]+)\]\((https?://[^\)]+)\)",
                "<a href='$2' target='_blank' style='color:#c8f135; font-weight:bold; text-decoration:none;'>$1 ↗</a>"
            );

            // Bozuk link: "Kaynağa Git ↗ target="_blank" style="...">En Ucuz Fiyatlara Bak"
            // Bu pattern'i temizle
            html = System.Text.RegularExpressions.Regex.Replace(
                html,
                @"Kaynağa Git ↗[^<]*>([^<]+)",
                "<a href='#' style='color:#c8f135; font-weight:bold; text-decoration:none;'>$1 ↗</a>"
            );

            // __[metin](url)__ formatı
            html = System.Text.RegularExpressions.Regex.Replace(
                html,
                @"__\[([^\]]+)\]\((https?://[^\)]+)\)__",
                "<a href='$2' target='_blank' style='color:#c8f135; font-weight:bold; text-decoration:none;'>$1 ↗</a>"
            );

            return html;
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
                    html.AppendLine($"<div style='font-size:15px; font-weight:700; color:#e8e8f0; margin:20px 0 8px; display:flex; align-items:center; gap:8px;'>{CleanMarkdown(line)}</div>");
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
                html.AppendLine($"<div style='color:#c8c8d8; line-height:1.8;'>{CleanMarkdown(line)}</div>");
            }

            if (inTable)
                html.AppendLine("</tbody></table></div>");

            return html.ToString();
        }

        private string FormatAdvice(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            // Kalın metin
            text = System.Text.RegularExpressions.Regex.Replace(
                text, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
            // Linkler
            text = System.Text.RegularExpressions.Regex.Replace(
                text, @"(https?://[^\s<]+)",
                "<a href='$1' target='_blank' style='color:var(--accent);'>$1</a>");
            // Satır sonları
            text = text.Replace("\n", "<br/>");
            return text;
        }

        private string CleanMarkdown(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            // Kalın metin
            text = System.Text.RegularExpressions.Regex.Replace(
                text, @"\*\*(.+?)\*\*", "<strong>$1</strong>");

            // İtalik
            text = System.Text.RegularExpressions.Regex.Replace(
                text, @"\*(.+?)\*", "<em>$1</em>");

            // [metin](url) formatı
            text = System.Text.RegularExpressions.Regex.Replace(
                text, @"\[(.+?)\]\((https?://[^\)]+)\)",
                "<a href='$2' target='_blank' style='color:var(--accent); text-decoration:none; font-weight:600;'>$1 ↗</a>");

            // Düz https:// linkleri — bozuk tırnak işaretlerini temizle
            text = System.Text.RegularExpressions.Regex.Replace(
                text, @"""?(https?://[^\s""<\)]+)""?",
                "<a href='$1' target='_blank' style='color:var(--accent); text-decoration:none; font-weight:600;'>Kaynağa Git ↗</a>");

            return text;
        }
    }

    public class HistoryItem
    {
        public string Query { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public string FirebaseKey { get; set; } = "";
        public string Advice { get; set; } = "";
    }
}