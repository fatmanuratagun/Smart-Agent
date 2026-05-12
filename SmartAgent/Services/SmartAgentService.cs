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
                string enrichedQuery = $"{userQuery} kullanıcı yorumu inceleme deneyim";

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

                string prompt = $@"Sen uzman bir e-ticaret danışmanısın. Adın Aura.

ÖNCEKİ KONUŞMALARIMIZ (Hafızan):
{_chatHistory}

KULLANICININ ŞİMDİKİ SORUSU: {userQuery}

İnternet Verileri:
{cleanData}

ADIM 1 — KATEGORİ TESPİT ET:
Kullanıcının sorusundaki ürün hangi kategoriye giriyor? Aşağıdan seç:

- TEKNOLOJİ (laptop, telefon, kulaklık, tablet, monitör, klavye, mouse, vs.)
  Değerlendirme kriterleri: İşlemci performansı, RAM, ekran kalitesi, pil ömrü, ısınma sorunu, FPS, garanti

- EV & MUTFAK (tencere, çaydanlık, fırın, buzdolabı, süpürge, vs.)
  Değerlendirme kriterleri: Malzeme kalitesi, yapışmazlık, taban kalınlığı, enerji tüketimi, garanti, kullanım kolaylığı

- GİYİM & AKSESUAR (ayakkabı, çanta, saat, gözlük, vs.)
  Değerlendirme kriterleri: Malzeme kalitesi, dikiş kalitesi, beden uyumu, renk seçeneği, fiyat/performans

- SPOR & OUTDOOR (bisiklet, spor ekipmanı, kamp malzemesi, vs.)
  Değerlendirme kriterleri: Dayanıklılık, ağırlık, kullanım rahatlığı, hava koşullarına dayanıklılık

- KOZMETİK & SAĞLIK (krem, vitamin, takviye, cihaz, vs.)
  Değerlendirme kriterleri: İçerik maddeleri, yan etkiler, kullanıcı deneyimi, sertifikasyon

- DİĞER (yukarıdakilere uymuyorsa)
  Değerlendirme kriterleri: Kalite, fiyat/performans, kullanıcı memnuniyeti, garanti

ADIM 2 — SORU YETERLİ Mİ KONTROL ET:
Eğer soru çok genelse (bütçe, amaç, tercih yok) → hiç ürün önerme, sadece 2-3 yönlendirici soru sor.

ADIM 3 — EĞER SORU YETERLİYSE ŞÖYLE YANIT VER:

🎯 Özet Tavsiye:
(Tek cümleyle net karar)

📊 Kategori Bazlı Değerlendirme Tablosu:
(Yukarıda tespit ettiğin kategorinin kriterlerine göre önerilen ürünleri karşılaştır)

| Kriter | [Ürün 1] | [Ürün 2] | [Ürün 3] |
|--------|----------|----------|----------|
| [Kriter 1] | ... | ... | ... |
| [Kriter 2] | ... | ... | ... |
| [Kriter 3] | ... | ... | ... |
| Fiyat | ... | ... | ... |
| Kullanıcı Puanı | ... | ... | ... |

🛒 Senin İçin Seçtiğim Ürünler:
1. [Ürün Adı] — [Fiyat]
   - Neden bu? [kısa açıklama]
   - Kullanıcılar ne demiş? [forum yorumlarından özet]
   - 🔗 Kaynak: [buraya linki yaz, boş bırakma]

2. [Ürün Adı] — [Fiyat]
   - Neden bu? [kısa açıklama]
   - Kullanıcılar ne demiş? [forum yorumlarından özet]
   - 🔗 Kaynak: [buraya linki yaz, boş bırakma]

⚠️ Dikkat Edilmesi Gereken:
(Bu kategoride alırken kesinlikle bakılması gereken 3 şey)

🚨 Fiyat Uyarısı:
(Eğer bu üründe sahte indirim, fiyat manipülasyonu veya güvenilmez satıcı riski varsa belirt. Yoksa bu bölümü kaldır.)";
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
                    .Take(15) // En fazla 15 cümle
                    .ToList();

                if (relevant.Count > 0)
                    return string.Join(". ", relevant);

                // Alakalı cümle bulunamazsa ilk 2000 karakter
                return text.Length > 2000 ? text.Substring(0, 2000) : text;
            }
            catch
            {
                return "";
            }
        }
    }
}