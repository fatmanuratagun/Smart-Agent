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

                var content = new StringContent($"{{\"q\":\"{query}\", \"gl\":\"tr\", \"hl\":\"tr\", \"num\": 3}}", Encoding.UTF8, "application/json");
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
                string searchResultsJson = await SearchWebAsync(userQuery);
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
                                cleanData += $"- {title}\n  Bilgi: {snippet}\n  Link: {link}\n\n";
                            }
                        }
                    }
                }
                catch { cleanData = searchResultsJson; }

                Debug.WriteLine("--- 3. AŞAMA: Gemini'ye DOĞRUDAN HTTP İsteği Hazırlanıyor... ---");

                string prompt = $@"Sen uzman bir e-ticaret danışmanısın. Kullanıcının sorusuna, internetten çektiğim şu güncel verileri kullanarak yanıt ver.

Kullanıcının Sorusu: {userQuery}

İnternet Verileri:
{cleanData}

GÖREVİN:
1. Kullanıcıya genel bir mantık sunduktan sonra, mutlaka internet verilerinde geçen EN MANTIKLI 2-3 ÜRÜNÜ ismen ve fiyatıyla öner.
2. Bu ürünlerin neden iyi olduğunu (kullanıcı yorumlarındaki olumlu puanlar, malzeme kalitesi vb.) kısaca belirt.
3. Eğer internet verilerinde link varsa, kullanıcının tıklayıp gidebileceği şekilde belirt.

YANIT FORMATIN ŞÖYLE OLSUN:
- 🎯 Özet Tavsiye: (Kısa bir cümleyle ne almalı?)
- ⚖️ Seçenek Analizi: (Neden bu seçenek?)
- 🛒 Senin İçin Seçtiğim Ürünler:
   1. [Ürün Adı] - [Fiyat] : (Neden bu? Kullanıcılar ne demiş?)
   2. [Ürün Adı] - [Fiyat] : (Neden bu? Kullanıcılar ne demiş?)
- ⚠️ Dikkat Edilmesi Gereken: (Alırken neye bakmalı?)";
                using var client = new HttpClient();

                // Gemini 2.5 Flash Doğrudan Bağlantı URL'si
                string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={_apiKey}";

                // İsteğin Gövdesini (Body) Oluşturuyoruz
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
                    return root.GetProperty("candidates")[0]
                               .GetProperty("content")
                               .GetProperty("parts")[0]
                               .GetProperty("text").GetString();
                }
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine("--- HATA OLUŞTU: " + ex.Message + " ---");
                return "Ajan bir sorunla karşılaştı: " + ex.Message;
            }
        }
    }
}