using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using System.Linq;
using System.Collections.Generic;

namespace SmartAgent.Services
{
    public class SmartAgentService
    {
        // ARTIK TEK BİR STRİNG YERİNE ANAHTAR HAVUZU (LIST) TUTUYORUZ
        private readonly List<string> _apiKeys;
        private readonly string _serperApiKey;
        private static string _chatHistory = "";

        public SmartAgentService(IConfiguration configuration)
        {
            // appsettings.json dosyasındaki virgüllü keyleri okuyup listeye çeviriyoruz
            var rawKeys = configuration["GeminiSettings:ApiKey"] ?? "";
            _apiKeys = rawKeys.Split(',')
                              .Select(k => k.Trim())
                              .Where(k => !string.IsNullOrEmpty(k))
                              .ToList();

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

                string enrichedQuery = $"{userQuery} en ucuz fiyat akakçe cimri yorum";
                string searchResultsJson = await SearchWebAsync(enrichedQuery);

                Debug.WriteLine("--- 2. AŞAMA: Arama bitti, sonuçlar geldi. ---");

                if (searchResultsJson == "Arama yapılamadı.") return "İnternete bağlanılamadı, API anahtarlarını kontrol et.";

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

                                string[] skipSources = {
                                    "trendyol.com", "hepsiburada.com", "amazon.com", "n11.com",
                                    "facebook.com", "twitter.com", "instagram.com", "youtube.com",
                                    "google.com", "wikipedia.org"
                                };

                                bool shouldSkip = skipSources.Any(s => link.Contains(s));

                                if (!shouldSkip && !string.IsNullOrEmpty(link))
                                {
                                    string pageContent = await FetchPageContentAsync(link, userQuery);

                                    bool isRelevant = false;
                                    if (!string.IsNullOrEmpty(pageContent))
                                    {
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
1. KATI BÜTÇE VE HAFIZA KURALI: Eğer İnternet Verilerinde (cleanData) bütçeye tam uygun ürün YOKSA, kendi yapay zeka hafızanı kullanarak öneri yap. FAKAT bütçe sınırını korumak için normalde pahalı olan premium/kablosuz modelleri (Örn: Logitech G Pro X Wireless, SteelSeries Arctis Nova 7 gibi 8.000 TL'lik ürünleri) KESİNLİKLE SEÇME ve bunların fiyatını feyk şekilde bütçeye uygunmuş gibi uydurma! Hafızandan ürün seçeceksen, o bütçe segmentinin gerçek ürünlerini (Örn: 5.000 TL bütçe için kablolu HyperX Cloud III, Razer BlackShark V2, SteelSeries Arctis Nova 1 veya 3 gibi gerçekten o banda yakın modelleri) seç.
2. LİNK KURALI: Aşağıdaki HTML'i AYNEN KOPYA, hiçbir karakter ekleme/çıkarma:
<a href=""https://www.akakce.com/arama/?q=urun+adi&az=1"" target=""_blank"" style=""color:#c8f135; font-weight:bold; text-decoration:none;"">Fiyatlara Bak ↗</a>
    *KRİTİK URL OPTİMİZASYONU*: ""urun+adi"" kısmını doldururken ""2-3 kişilik"", ""otomatik"", ""kamp"", ""gaming"" gibi uzun ve gereksiz sıfatları temizle. FAKAT markanın diğer sektörlerdeki ürünleriyle karışmaması için (Örn: Çadır yerine buz sandığı, laptop yerine monitör çıkmaması için) MUTLAKA şu formatı koru: ""Marka + Varsa Model + Tek Kelime Ana Kategori"". Kelimelerin arasına artı (+) işareti koy.
    Doğru Örnekler:
    - ""Coleman+Cobra+Cadir"" (Sadece ""Coleman+Cobra"" yazma, buz sandığı çıkar!)
    - ""Quechua+Arpenaz+Cadir""
    - ""HP+Victus+Laptop""
    - ""Xiaomi+Airfryer""
    YASAK: Markdown link [metin](url) KULLANMA. Sadece HTML <a> tag kullan.
Google linki — kullanıcı tam ürünü bulsun diye:
<a href=""https://www.google.com/search?q=urun+adi+buraya+fiyat+satin+al&gl=tr"" target=""_blank"" style=""color:#7c6fff; font-weight:bold; text-decoration:none; margin-left:10px;"">Google'da Ara ↗</a>

3. HTML ZORUNLULUĞU: Markdown (**, *, #) YASAKTIR. Sadece HTML (<b>, <h3>, <p>, <table>) kullan.
4. JARGON: ""5"" = 5.000 TL.
5. BÜTÇE KORUMA KURALI: Kullanıcı bir bütçe belirttiyse (Örn: ""3000 TL"", ""5 bin TL""), 
   önerdiğin ürünlerin fiyatı bu bütçeyi aşıyorsa cevabın EN BAŞINA şu uyarıyı ekle:
   <div style=""background:rgba(229,62,62,0.1); border:1px solid #e53e3e; border-radius:10px; 
               padding:12px 16px; margin-bottom:16px; color:#fc8181; font-size:13px;"">
   ⚠️ <b>Bütçe Uyarısı:</b> Önerdiğim ürünler belirttiğin bütçeyi aşıyor olabilir. 
   Piyasa koşulları nedeniyle bu bütçeyle seçenekler kısıtlı — 
   sana en yakın fiyatlı modelleri getirdim.
   </div>
   Eğer tüm öneriler bütçe içindeyse bu uyarıyı KOYMA.
6. FİYAT KURALI VE RAKAM UYDURMA YASAĞI: Fiyat alanına rakamsal bir değer (Örn: 4.500 TL veya 5.000 TL) yazacaksan bunu KESİNLİKLE sadece İNTERNET VERİLERİ'nden (cleanData) almalısın. Eğer internet verilerinde o ürüne ait net bir fiyat dönmediyse, fiyat kısmına KESİNLİKLE kendi hafızandan tahmini veya hayali rakamlar YAZMA! Fiyat satırına/sütununa sadece ""Güncel fiyat için siteyi ziyaret edin"" yaz. Rakam uydurmak kesinlikle yasaktır.
7. KONU VE KATEGORİ KORUMA KURALI: Kullanıcı aynı sorgu içinde tamamen alakasız iki farklı kategori sorsa bile (Örn: ""Kamp çadırı ve kedi maması""), KESİNLİKLE kategorileri karıştırma! Sadece ana e-ticaret ürününe (Örn: Kamp çadırına) odaklan. Kedi maması kısmını tamamen görmezden gel veya ""Ben sadece teknoloji/kamp/moda gibi ana e-ticaret ürünlerinde uzmanım, kedi maması öneremem"" diyerek kibar ortak bir cümleyle geçiştir. Cevapta asla iki farklı kategoriye ait tablo veya liste OLAMAZ.
8. GIZLI LİNK KURALI: Eğer kullanıcı sadece link gönderirse ve sen ne linkin metninden ne de ""cleanData"" (İnternet Verileri) içinden ürünün ne olduğunu KESİNLİKLE anlayamazsan, 
asla ürün uydurma veya tablo çizme. Sadece şu HTML mesajını ver:
<p>Linklerin içindeki ürün detaylarına şu an ulaşamıyorum. Bana ürünlerin marka ve modellerini yazarsan senin için harika bir karşılaştırma yapabilirim! 🔍</p>

9. BÜTÇE UÇURUMU VE SPESİFİK MARKA MODEL ZORUNLULUĞU KURALI:
   - Kullanıcının istediği spesifik donanım/ürün ile belirttiği bütçe arasında UÇURUM/İMKANSIZLIK varsa (Örn: 1.000 TL'ye RTX 4050 laptop istemek), kesinlikle hayali fiyatlar uydurma veya 1.000 TL'ye Chromebook satmaya çalışma! Kullanıcıya bu bütçeyle o ürünün alınmasının imkansız olduğunu dürüst ve net bir şekilde söyle.
   - Kullanıcının bütçesi o kategori için MAKUL ve GERÇEKÇİ ise (Örn: 30.000 TL bütçeye laptop istemek), kullanıcının istediği model bütçeyi aşsa bile kestirip atma! KESİNLİKLE ""Giriş Seviyesi Oyun Dizüstü"" gibi yuvarlak genel kategori adları yazma. İnternet verilerini tara veya kendi güçlü hafızanı kullanıp o bütçeye (Örn: 30.000 TL'ye) gerçekten alınabilecek en iyi ve GERÇEK SPESİFİK MARKA/MODELLERİ (Örn: ""HP Victus 15"", ""Acer Nitro 5"", ""Lenovo LOQ"" gibi) doğrudan tam isimleriyle seçerek kullanıcıya öner.
   - Önerdiğin her ürünün adı mutlaka TEK VE GERÇEK BİR MODEL olmalı. Asla ""Everest / Piranha / Ranger"" gibi markaları eğik çizgiyle birleştirerek yuvarlak grup isimleri yazma. Sadece tek birini seç: Örn: ""Quechua Arpenaz 3"". 
*ÖNEMLİ MARKA SEÇİM KURALI: Önerdiğin markaların Akakçe'de kesinlikle karşılığı olmalı. Bilgisayarda (HP, ASUS, Lenovo, Acer, Dell), Çadırda (Quechua, Coleman, Husky, Decathlon) gibi piyasada resmi kataloğu bulunan büyük ve bilindik markaları seç. Everest gibi sadece pazaryerlerinde spot satılan fason markaları asla listeleme.*

ADIM 1 — YANIT FORMATI SEÇİMİ:
Aşağıdaki 4 durumdan birine uygun formatta SADECE HTML ile yanıt ver!

--- DURUM 1: GENEL SORU ---
Kullanıcının sorusunda şunlardan HERHANGİ BİRİ EKSİKSE bu durumu kullan:
- Ürün tipi/modeli net değil (sırt mı, yan mı, omuz mu, gaming mi?)
- Kullanım amacı belli değil (okul, iş, spor, günlük?)
- Marka tercihi sorulmadı

SADECE şu formatta 2-3 soru sor, ürün önerme:
<p>Harika! Sana en uygun seçeneği bulabilmem için birkaç sorum var:</p>
<ul>
  <li>[Soru 1 — ürün tipi: sırt mı, yan mı, omuz mu?]</li>
  <li>[Soru 2 — kullanım amacı: okul, iş, spor, günlük?]</li>
  <li>[Soru 3 — marka veya özel tercih var mı?]</li>
</ul>

--- DURUM 2: SPESİFİK ÜRÜN (Alınır mı?) ---
<p><b>🎯 Alınır mı?:</b> (Tek cümle)</p>
<p><b>🔍 Ürün Analizi:</b> (Artı ve eksiler)</p>
<p><b>💡 Alternatif:</b> (Varsa öner ve temiz HTML link ver)</p>

--- DURUM 3: TAVSİYE İSTEĞİ (2 veya 3 Ürün) ---
<div style=""background:rgba(229,62,62,0.1); border:1px solid #e53e3e; border-radius:10px; 
            padding:12px 16px; margin-bottom:16px; color:#fc8181; font-size:13px;"">
⚠️ <b>Bütçe Uyarısı:</b> Önerdiğim ürünler belirttiğin bütçeyi aşıyor olabilir. 
Piyasa koşulları nedeniyle bu bütçeyle seçenekler kısıtlı — sana en yakın fiyatlı modelleri getirdim.
</div>
BU UYARI KUTUSUNU SADECE önerilen ürün fiyatı kullanıcının bütçesini aşıyorsa göster. Bütçe içindeyse tamamen sil.
<h3>🎯 Özet Tavsiye</h3>
<p>(Tek cümle giriş)</p>

<h3>📊 Karşılaştırma Tablosu</h3>
<table style=""width:100%; border-collapse: collapse; margin-bottom: 20px; text-align: left;"" border=""1"">
  <tr style=""background-color: #f3f4f6;"">
    <th style=""padding: 12px; border: 1px solid #e5e7eb;"">Kriter</th>
    <th style=""padding: 12px; border: 1px solid #e5e7eb;"">[Ürün 1 Tam Model Adı]</th>
    <th style=""padding: 12px; border: 1px solid #e5e7eb;"">[Ürün 2 Tam Model Adı]</th>
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
  <b>1. [BURAYA ASLA GENEL KATEGORİ YAZMA, GERÇEK MARKA VE MODEL YAZ. Örn: HP Victus 16]</b><br>
  💡 <b>Neden bu?:</b> [Açıklama]<br>
  🗣️ <b>Yorumlar:</b> [Özet]<br>
  <a href=""https://www.akakce.com/arama/?q=urunun+tam+adi"" target=""_blank"" style=""color:#2563eb; font-weight:bold; text-decoration:underline;"">En Ucuz Fiyatlara Bak</a>
</div>

<div style=""margin-bottom: 16px;"">
  <b>2. [BURAYA ASLA GENEL KATEGORİ YAZMA, GERÇEK MARKA VE MODEL YAZ. Örn: Acer Nitro 5]</b><br>
  💡 <b>Neden bu?:</b> [Açıklama]<br>
  🗣️ <b>Yorumlar:</b> [Özet]<br>
  <a href=""https://www.akakce.com/arama/?q=urunun+tam+adi"" target=""_blank"" style=""color:#2563eb; font-weight:bold; text-decoration:underline;"">En Ucuz Fiyatlara Bak</a>
</div>

--- DURUM 4: İKİ ÜRÜN KIYASLAMA (VS) ---
Kullanıcı iki farklı ürün veya link verip ""Hangisi?"", ""Sence bu mu bu mu?"" diye sorarsa BU DURUMU KULLAN.

<h3>⚔️ Karşılaştırma: [Ürün 1 Kısa Adı] vs [Ürün 2 Kısa Adı]</h3>
<p>(İki ürünün genel rekabeti hakkında tek cümlelik giriş)</p>

<h3>📊 Özellik Tablosu</h3>
<table style=""width:100%; border-collapse: collapse; margin-bottom: 20px; text-align: left;"" border=""1"">
  <tr style=""background-color: #f3f4f6;"">
    <th style=""padding: 12px; border: 1px solid #e5e7eb;"">Kriter</th>
    <th style=""padding: 12px; border: 1px solid #e5e7eb;"">[Ürün 1]</th>
    <th style=""padding: 12px; border: 1px solid #e5e7eb;"">[Ürün 2]</th>
  </tr>
  <tr>
    <td style=""padding: 12px; border: 1px solid #e5e7eb;""><b>Kullanım/Tutuş</b></td>
    <td style=""padding: 12px; border: 1px solid #e5e7eb;"">...</td>
    <td style=""padding: 12px; border: 1px solid #e5e7eb;"">...</td>
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

<h3>🏆 Aura'nın Kararı</h3>
<div style=""background-color: rgba(59, 130, 246, 0.08); padding: 15px; border-left: 4px solid #3b82f6; border-radius: 4px;"">
  <p><b>Kimi Seçmelisin?:</b> Eğer önceliğin [Özellik 1] ise <b>[Ürün 1]</b>, ama [Özellik 2] senin için daha önemliyse kesinlikle <b>[Ürün 2]</b> modelini almalısın.</p>
</div>";

                using var client = new HttpClient();
                string finalAnswer = null;
                bool isRequestSuccessful = false;

                // --- YENİ EKLENEN TRY-CATCH ANAHTAR HAVUZU DÖNGÜSÜ ---
                foreach (var currentKey in _apiKeys)
                {
                    try
                    {
                        // gemini-3-flash yerine tam stabil olan gemini-2.5-flash yazıyoruz:
                        string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={currentKey}";
                        
                        var requestBody = new
                        {
                            contents = new[]
                            {
                                new { parts = new[] { new { text = prompt } } }
                            }
                        };

                        string jsonPayload = JsonSerializer.Serialize(requestBody);
                        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                        Debug.WriteLine($"--- 4. AŞAMA: Google Sunucularına Bağlanılıyor (Key: {currentKey.Substring(0, System.Math.Min(5, currentKey.Length))}...) ---");
                        var response = await client.PostAsync(url, content);
                        string responseString = await response.Content.ReadAsStringAsync();

                        if (response.IsSuccessStatusCode)
                        {
                            Debug.WriteLine("--- 5. AŞAMA: Gemini cevabı başarıyla üretti! ---");

                            using (JsonDocument doc = JsonDocument.Parse(responseString))
                            {
                                var root = doc.RootElement;
                                finalAnswer = root.GetProperty("candidates")[0]
                                                         .GetProperty("content")
                                                         .GetProperty("parts")[0]
                                                         .GetProperty("text").GetString();
                            }

                            isRequestSuccessful = true;
                            break; // Başarılı olunduğu için döngüden çık, sonraki keyleri deneme
                        }

                        Debug.WriteLine($"⚠️ Anahtar hata döndürdü ({response.StatusCode}). Sonraki yedek anahtara geçiliyor...");
                    }
                    catch (System.Exception ex)
                    {
                        Debug.WriteLine($"🚨 Anahtar isteği sırasında teknik hata oluşti: {ex.Message}. Sonraki anahtara geçiliyor...");
                    }
                }

                // Eğer havuzdaki hiçbir anahtar çalışmadıysa veya cevap boşsa hata döndür
                if (!isRequestSuccessful || string.IsNullOrEmpty(finalAnswer))
                {
                    return "<p>Şu anda yoğunluk nedeniyle isteklerinize cevap veremiyorum. Lütfen birkaç dakika sonra tekrar deneyin veya sistem yöneticisiyle iletişime geçin. 🛑</p>";
                }

                _chatHistory += $"Kullanıcı: {userQuery}\nAura: {finalAnswer}\n---\n";
                return finalAnswer;
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

                var text = System.Text.RegularExpressions.Regex.Replace(response, "<[^>]*>", " ");
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

                var sentences = text.Split(new[] { '.', '!', '?' },
                    StringSplitOptions.RemoveEmptyEntries);

                var relevant = sentences
                    .Where(s => keyword.ToLower().Split(' ')
                                       .Any(w => w.Length > 3 && s.ToLower().Contains(w)))
                    .Take(10)
                    .ToList();

                if (relevant.Count > 0)
                    return string.Join(". ", relevant);

                return text.Length > 1500 ? text.Substring(0, 1500) : text;
            }
            catch
            {
                return "";
            }
        }
    }
}