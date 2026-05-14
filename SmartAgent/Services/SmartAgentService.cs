using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
// DİKKAT: Eski Mscc.GenerativeAI kütüphanesini sildiğimiz için using kısmından da kaldırdık.

namespace SmartAgent.Services
{
    public class SmartAgentService
    {
        private readonly string _apiKey;
        private readonly string _serperApiKey;
        // YENİ EKLENEN HAFIZA DEĞİŞKENİ (Aura'nın Not Defteri)
        private static string _chatHistory = "";


        public SmartAgentService(IConfiguration configuration)
        {
            _apiKey = configuration["GeminiSettings:ApiKey"];
            _serperApiKey = configuration["SerperSettings:ApiKey"];
        }

        public async Task<string> SearchWebAsync(string query)
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = System.TimeSpan.FromSeconds(15);

                var request = new HttpRequestMessage(HttpMethod.Post, "https://google.serper.dev/search");
                request.Headers.Add("X-API-KEY", _serperApiKey);

                var content = new StringContent($"{{\"q\":\"{query}\", \"gl\":\"tr\", \"hl\":\"tr\", \"num\": 8}}", Encoding.UTF8, "application/json");
                request.Content = content;

                var response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode();

                return await response.Content.ReadAsStringAsync();
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine("SERPER ARAMA HATASI: " + ex.Message);
                return "Arama yapılamadı.";
            }
        }

        public async Task<string> GetShoppingAdviceAsync(string userQuery)
        {
            try
            {
                Debug.WriteLine("--- 1. AŞAMA: İnternette arama başlatılıyor... ---");

                // Ürün adını soru içinden çıkarmak için Gemini'ye sormak yerine
                // direkt sorguyu daha akıllı kur
                // Artık Google'a sadece yorumları değil, Akakçe ve Cimri'deki güncel fiyatları da getirmesini emrediyoruz!
                string enrichedQuery = $"{userQuery} en ucuz fiyat akakçe cimri yorum";

                // ESKİ HALİ: await SearchWebAsync(userQuery);
                // YENİ HALİ: Artık zenginleştirilmiş sorguyu aratıyoruz!
                string searchResultsJson = await SearchWebAsync(enrichedQuery);

                Debug.WriteLine("--- 2. AŞAMA: Arama bitti, sonuçlar geldi. ---");

                if (searchResultsJson == "Arama yapılamadı.") return "İnternete bağlanılamadı, API anahtarlarını kontrol et.";

                // Veriyi Temizleme İşlemi
                string cleanData = "";
                try
                {
                    using (JsonDocument doc = JsonDocument.Parse(searchResultsJson))
                    {
                        if (doc.RootElement.TryGetProperty("organic", out JsonElement organicResults))
                        {
                            foreach (JsonElement result in organicResults.EnumerateArray())
                            {
                                string title = result.TryGetProperty("title", out JsonElement t) ? t.GetString() : "";
                                string snippet = result.TryGetProperty("snippet", out JsonElement s) ? s.GetString() : "";
                                string link = result.TryGetProperty("link", out JsonElement l) ? l.GetString() : "";
                                // Güvenilir yorum kaynaklarını öncelikle çek
                                // Atlanacak siteler — reklam, sosyal medya, e-ticaret platformları
                                string[] skipSources = {
    "trendyol.com", "hepsiburada.com", "amazon.com", "n11.com",
    "facebook.com", "twitter.com", "instagram.com", "youtube.com",
    "google.com", "wikipedia.org"
};

                                bool shouldSkip = skipSources.Any(s => link.Contains(s));

                                if (!shouldSkip && !string.IsNullOrEmpty(link))
                                {
                                    string pageContent = await FetchPageContentAsync(link, userQuery);

                                    // Sayfa içeriği kullanıcının sorusuyla alakalı mı kontrol et
                                    bool isRelevant = false;
                                    if (!string.IsNullOrEmpty(pageContent))
                                    {
                                        // Kullanıcının sorgusundaki kelimelerin en az birini içeriyor mu?
                                        var queryWords = userQuery.ToLower()
                                                                  .Split(' ')
                                                                  .Where(w => w.Length > 3)
                                                                  .ToList();
                                        isRelevant = queryWords.Any(w => pageContent.ToLower().Contains(w));
                                    }

                                    if (isRelevant)
                                    {
                                        cleanData += $"- BAŞLIK: {title}\n  KAYNAK LİNK: {link}\n  İÇERİK: {pageContent}\n\n";
                                    }
                                    else
                                    {
                                        // Alakasızsa sadece snippet kullan
                                        cleanData += $"- BAŞLIK: {title}\n  KAYNAK LİNK: {link}\n  BİLGİ: {snippet}\n\n";
                                    }
                                }
                            }
                        }
                    }
                }
                catch { cleanData = searchResultsJson; }

                Debug.WriteLine("--- 3. AŞAMA: Gemini'ye DOĞRUDAN HTTP İsteği Hazırlanıyor... ---");

                string prompt = $@"Sen uzman, dürüst ve samimi bir e-ticaret danışmanı Aura'sın.

ÖNCEKİ KONUŞMALAR:
{_chatHistory}

KULLANICI SORUSU: {userQuery}

İNTERNET VERİLERİ:
{cleanData}

🚨 HAYATİ KURALLAR (BUNLARA UYMAZSAN SİSTEM ÇÖKER):
1. ESNEK HAFIZA KURALI: Eğer İnternet Verilerinde (cleanData) bütçeye uygun ve fiyatı belli olan ürün YOKSA, asla ""ürün bulamadım"" deme! Kendi yapay zeka hafızanı kullanarak Türkiye'de satılan ve kullanıcının bütçesine (Örn: 5.000 TL) uygun olan 2 veya 3 ürünü KENDİN ÖNER. Fiyatı net bilmiyorsan tabloya ""Ortalama 3.000-4.000 TL"" gibi tahmini bir fiyat yaz.
2. LİNK KURALI (BOZULMAZ HTML): Link kodunun içine, sağına veya soluna KESİNLİKLE EMOJİ KOYMA! Tırnakları bozma. SADECE şu temiz HTML formatını birebir kullan:
   <a href=""https://www.akakce.com/arama/?q=urunun+tam+adi"" target=""_blank"" style=""color:#2563eb; font-weight:bold; text-decoration:underline;"">En Ucuz Fiyatlara Bak</a>
3. HTML ZORUNLULUĞU: Markdown (**, *, #) YASAKTIR. Sadece HTML (<b>, <h3>, <p>, <table>) kullan.
4. JARGON: ""5"" = 5.000 TL.

ADIM 1 — YANIT FORMATI SEÇİMİ:
Aşağıdaki 3 durumdan birine uygun formatta SADECE HTML ile yanıt ver!

--- DURUM 1: GENEL SORU ---
<p>Sadece 1-2 cümleyle bütçe veya amaç sor. Ürün önerme.</p>

--- DURUM 2: SPESİFİK ÜRÜN (Alınır mı?) ---
<p><b>🎯 Alınır mı?:</b> (Tek cümle)</p>
<p><b>🔍 Ürün Analizi:</b> (Artı ve eksiler)</p>
<p><b>💡 Alternatif:</b> (Varsa öner ve temiz HTML link ver)</p>

--- DURUM 3: TAVSİYE İSTEĞİ (2 veya 3 Ürün) ---
<h3>🎯 Özet Tavsiye</h3>
<p>(Tek cümle giriş)</p>

<h3>📊 Karşılaştırma Tablosu</h3>
<table style=""width:100%; border-collapse: collapse; margin-bottom: 20px; text-align: left;"" border=""1"">
  <tr style=""background-color: #f3f4f6;"">
    <th style=""padding: 12px; border: 1px solid #e5e7eb;"">Kriter</th>
    <th style=""padding: 12px; border: 1px solid #e5e7eb;"">[Ürün 1]</th>
    <th style=""padding: 12px; border: 1px solid #e5e7eb;"">[Ürün 2]</th>
  </tr>
  <tr>
    <td style=""padding: 12px; border: 1px solid #e5e7eb;""><b>Fiyat</b></td>
    <td style=""padding: 12px; border: 1px solid #e5e7eb;"">[Fiyat veya Tahmin]</td>
    <td style=""padding: 12px; border: 1px solid #e5e7eb;"">[Fiyat veya Tahmin]</td>
  </tr>
  <tr>
    <td style=""padding: 12px; border: 1px solid #e5e7eb;""><b>Artısı</b></td>
    <td style=""padding: 12px; border: 1px solid #e5e7eb;"">...</td>
    <td style=""padding: 12px; border: 1px solid #e5e7eb;"">...</td>
  </tr>
  <tr>
    <td style=""padding: 12px; border: 1px solid #e5e7eb;""><b>Eksisi</b></td>
    <td style=""padding: 12px; border: 1px solid #e5e7eb;"">...</td>
    <td style=""padding: 12px; border: 1px solid #e5e7eb;"">...</td>
  </tr>
</table>

<h3>🛒 Senin İçin Seçtiğim Ürünler</h3>
<div style=""margin-bottom: 16px;"">
  <b>1. [Ürün Adı]</b><br>
  💡 <b>Neden bu?:</b> [Açıklama]<br>
  🗣️ <b>Yorumlar:</b> [Özet]<br>
  <a href=""https://www.akakce.com/arama/?q=urunun+tam+adi"" target=""_blank"" style=""color:#2563eb; font-weight:bold; text-decoration:underline;"">En Ucuz Fiyatlara Bak</a>
</div>

<div style=""margin-bottom: 16px;"">
  <b>2. [Ürün Adı]</b><br>
  💡 <b>Neden bu?:</b> [Açıklama]<br>
  🗣️ <b>Yorumlar:</b> [Özet]<br>
  <a href=""https://www.akakce.com/arama/?q=urunun+tam+adi"" target=""_blank"" style=""color:#2563eb; font-weight:bold; text-decoration:underline;"">En Ucuz Fiyatlara Bak</a>
</div>";

                using var client = new HttpClient();

                // Gemini 2.5 Flash Doğrudan Bağlantı URL'si
                string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={_apiKey}";

                var requestBody = new
                {
                    contents = new[]
                    {
        new { parts = new[] { new { text = prompt } } }
    }
                };

                string jsonPayload = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                Debug.WriteLine("--- 4. AŞAMA: Google Sunucularına Bağlanılıyor... ---");
                var response = await client.PostAsync(url, content);
                string responseString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine("--- API HATASI --- \n" + responseString);
                    return "Gemini API Hatası: " + response.StatusCode;
                }

                Debug.WriteLine("--- 5. AŞAMA: Gemini cevabı başarıyla üretti! ---");

                using (JsonDocument doc = JsonDocument.Parse(responseString))
                {
                    var root = doc.RootElement;
                    string finalAnswer = root.GetProperty("candidates")[0]
                                             .GetProperty("content")
                                             .GetProperty("parts")[0]
                                             .GetProperty("text").GetString();

                    // SİHİRLİ DOKUNUŞ: Cevabı ekrana basmadan önce hafızaya (not defterine) yazıyoruz!
                    _chatHistory += $"Kullanıcı: {userQuery}\nAura: {finalAnswer}\n---\n";

                    return finalAnswer;
                }
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine("--- HATA OLUŞTU: " + ex.Message + " ---");
                return "Ajan bir sorunla karşılaştı: " + ex.Message;
            }

        }
        public async Task<string> FetchPageContentAsync(string url, string keyword)
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = System.TimeSpan.FromSeconds(10);
                client.DefaultRequestHeaders.Add("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                var response = await client.GetStringAsync(url);

                // HTML temizle
                var text = System.Text.RegularExpressions.Regex.Replace(response, "<[^>]*>", " ");
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

                // Keyword geçen paragrafları bul, sadece onları al
                var sentences = text.Split(new[] { '.', '!', '?' },
                                           StringSplitOptions.RemoveEmptyEntries);

                var relevant = sentences
                    .Where(s => keyword.ToLower().Split(' ')
                                       .Any(w => w.Length > 3 && s.ToLower().Contains(w)))
                    .Take(10) // En fazla 10 cümle
                    .ToList();

                if (relevant.Count > 0)
                    return string.Join(". ", relevant);

                // Alakalı cümle bulunamazsa ilk 2000 karakter
                return text.Length > 1500 ? text.Substring(0, 1500) : text;
            }
            catch
            {
                return "";
            }
        }
    }
}