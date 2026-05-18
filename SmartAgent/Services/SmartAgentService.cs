using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Reactive;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using static System.Net.Mime.MediaTypeNames;
using static Google.Api.ResourceDescriptor.Types;

namespace SmartAgent.Services
{
    public class SmartAgentService
    {
        private readonly List<string> _apiKeys;
        private readonly string _serperApiKey;
        private static string _chatHistory = "";

        public SmartAgentService(IConfiguration configuration)
        {
            var rawKeys = configuration["GeminiSettings:ApiKey"] ?? "";
            _apiKeys = rawKeys.Split(',')
                              .Select(k => k.Trim())
                              .Where(k => !string.IsNullOrEmpty(k))
                              .ToList();

            _serperApiKey = configuration["SerperSettings:ApiKey"];
        }


        // =========================================
        // 🧠 1. BÜTÇE ALGILAMA (Kullanıcının mesajından bütçeyi bulur)
        // =========================================
        private int ExtractBudget(string text)
        {
            text = text.ToLower();

            var matchK = Regex.Match(text, @"(\d+)\s*k");
            if (matchK.Success) return int.Parse(matchK.Groups[1].Value) * 1000;

            var matchBin = Regex.Match(text, @"(\d+)\s*bin");
            if (matchBin.Success) return int.Parse(matchBin.Groups[1].Value) * 1000;

            var matchPlain = Regex.Match(text, @"\b(\d{4,6})\b");
            if (matchPlain.Success) return int.Parse(matchPlain.Groups[1].Value);

            var small = Regex.Match(text, @"\b(\d{1,2})\b");
            if (small.Success) return int.Parse(small.Groups[1].Value) * 1000;

            return 0;
        }

        // =========================================
        // 💰 2. FİYAT ALGILAMA (İnternet sitesindeki fiyatı okur)
        // =========================================
        private int ExtractPrice(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            text = text.Replace(".", "");

            var matches = Regex.Matches(text, @"(\d{1,3}(?:\.\d{3})+|\d{3,6})\s*TL", RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                string raw = match.Groups[1].Value.Replace(".", "");
                if (int.TryParse(raw, out int price) && price > 500)
                {
                    return price;
                }
            }
            return 0;
        }

        // =========================================
        // 🌐 3. WEB SEARCH (Serper API)
        // =========================================
        public async Task<string> SearchWebAsync(string query)
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(15);

                var request = new HttpRequestMessage(HttpMethod.Post, "https://google.serper.dev/search");
                request.Headers.TryAddWithoutValidation("X-API-KEY", _serperApiKey);

                var content = new StringContent(
                    JsonSerializer.Serialize(new { q = query, gl = "tr", hl = "tr", num = 8 }),
                    Encoding.UTF8, "application/json");
                request.Content = content;

                var response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("SERPER ARAMA HATASI: " + ex.Message);
                return "Arama yapılamadı.";
            }
        }

        // =========================================
        // 🚀 4. ANA SİSTEM (GetShoppingAdviceAsync)
        // =========================================
        public async Task<string> GetShoppingAdviceAsync(string userQuery)
        {
            try
            {
                Debug.WriteLine("--- 1. AŞAMA: İnternette arama başlatılıyor... ---");

                int userBudget = ExtractBudget(userQuery);
                Debug.WriteLine($"💰 Kod Tarafından Algılanan Bütçe: {userBudget} TL");

                string enrichedQuery = $"{userQuery} en ucuz fiyat akakçe yorum";
                string searchResultsJson = await SearchWebAsync(enrichedQuery);

                if (searchResultsJson == "Arama yapılamadı.")
                {
                    searchResultsJson = @"{ ""organic"": [] }"; // Çökmeyi engelle
                }

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

                                // FORUMLARI SERBEST BIRAKTIK (Yorumlar oradan gelecek)
                                string[] skipSources = { "trendyol.com", "hepsiburada.com", "amazon.com", "n11.com", "facebook.com", "twitter.com", "instagram.com", "youtube.com", "wikipedia.org" };
                                if (skipSources.Any(src => link.Contains(src)) || string.IsNullOrEmpty(link))
                                    continue;

                                string pageContent = await FetchPageContentAsync(link, userQuery);
                                int detectedPrice = ExtractPrice(pageContent + " " + snippet);

                                // 🔥 BÜTÇE KALKANI: Eğer site bütçeyi %15'ten fazla aşıyorsa Gemini'a GÖNDERME!
                                if (userBudget > 0 && detectedPrice > 0 && detectedPrice > userBudget * 1.15)
                                {
                                    Debug.WriteLine($"❌ Bütçe aşıldı, bu site çöpe atıldı: {detectedPrice} TL");
                                    continue;
                                }

                                bool isRelevant = !string.IsNullOrEmpty(pageContent) &&
                                                  userQuery.ToLower().Split(' ').Where(w => w.Length > 3).Any(w => pageContent.ToLower().Contains(w));

                                if (isRelevant)
                                {
                                    cleanData += $"- BAŞLIK: {title}\n  FİYAT: {(detectedPrice > 0 ? detectedPrice + " TL" : "Bulunamadı")}\n  LİNK: {link}\n  İÇERİK: {pageContent}\n\n";
                                }
                                else
                                {
                                    cleanData += $"- BAŞLIK: {title}\n  FİYAT: {(detectedPrice > 0 ? detectedPrice + " TL" : "Bulunamadı")}\n  LİNK: {link}\n  BİLGİ: {snippet}\n\n";
                                }
                            }
                        }
                    }
                }
                catch { cleanData = "Filtreleme sonrası uygun ürün bulunamadı."; }

                Debug.WriteLine("--- 3. AŞAMA: Gemini'ye İsteği Hazırlanıyor... ---");

                // DEV PROMPT (Düzeltilmiş, Stabil Versiyon)
                string prompt = $@"Sen uzman, dürüst ve samimi bir e-ticaret danışmanı Aura'sın.

ÖNCEKİ KONUŞMALAR:
{_chatHistory}

KULLANICI SORUSU: {userQuery}
KULLANICININ HESAPLANAN BÜTÇESİ: {(userBudget > 0 ? userBudget + " TL" : "Belirtilmedi")}

İNTERNET VERİLERİ (Bütçeye uygun filtrelenmiş veriler):
{cleanData}

🚨 HAYATİ KURALLAR (BUNLARA UYMAZSAN SİSTEM ÇÖKER):
1. BÜTÇE VE İNTERNET VERİSİ KURALI: İnternet verileri (cleanData) backend tarafından kullanıcının bütçesine göre filtrelenerek sana ulaştı. Eğer veride uygun ürün yoksa, KENDİ HAFIZANDAN kullanıcının bütçesine GERÇEKTEN UYAN modelleri getir. Bütçeyi aşan premium modelleri KESİNLİKLE önerme.
2. LİNK KURALI: Aşağıdaki HTML'i AYNEN KOPYA, hiçbir karakter ekleme/çıkarma:
<a href=""https://www.akakce.com/arama/?q=urun+adi&az=1"" target=""_blank"" style=""color:#c8f135; font-weight:bold; text-decoration:none;"">Fiyatlara Bak ↗</a>
    *KRİTİK URL OPTİMİZASYONU*: ""urun+adi"" kısmını doldururken ""2-3 kişilik"", ""otomatik"", ""kamp"", ""gaming"" gibi uzun sıfatları temizle. FORMAT: ""Marka + Varsa Model + Tek Kelime Ana Kategori"". Artı (+) işareti kullan. (Örn: Coleman+Cobra+Cadir). Markdown KULLANMA. Sadece HTML <a> tag kullan.
Google linki:
<a href=""https://www.google.com/search?q=urun+adi+buraya+fiyat+satin+al&gl=tr"" target=""_blank"" style=""color:#7c6fff; font-weight:bold; text-decoration:none; margin-left:10px;"">Google'da Ara ↗</a>

3. HTML ZORUNLULUĞU: Markdown (**, *, #) YASAKTIR. Sadece HTML (<b>, <h3>, <p>, <table>) kullan.
4. BÜTÇE KORUMA UYARISI: Eğer kullanıcının bütçesi aşırı düşükse ve mecburen %15-20 aşıyorsan, cevabın EN BAŞINA HTML uyarı kutusunu (⚠️ Bütçe Uyarısı) ekle. Önerilerin bütçe içindeyse KESİNLİKLE EKLEME.
5. KATI FİYAT VE 'SAYI UYDURMA' YASAĞI: Önerdiğin ürünlerin fiyatını İNTERNET VERİLERİ (cleanData) içinde net rakam olarak göremiyorsan, fiyat satırına KESİNLİKLE hafızandan tahmini rakam YAZMA! Sadece ""Güncel fiyat için Akakçe linkini ziyaret edin"" yaz. Rakam uydurmak kesinlikle yasaktır!
6.KONU VE KATEGORİ KORUMA: Alakasız iki kategori sorulursa(Örn: Çadır ve Kedi Maması) sadece ana e - ticaret ürününe odaklan, diğerini kibarca reddet.
7.GİZLİ LİNK KURALI: Kullanıcı sadece kırık / anlamsız link gönderirse asla ürün uydurma.Sadece şu HTML mesajını ver: < p > Linklerin içindeki ürün detaylarına şu an ulaşamıyorum. Bana ürünlerin marka ve modellerini yazarsan senin için harika bir karşılaştırma yapabilirim! 🔍</ p >
8.SPESİFİK MARKA / MODEL ZORUNLULUĞU: Tablolara ve listelere asla ""Teflon Tencere"", ""Oyun Bilgisayarı"" gibi genel isimler yazma!Piyasada karşılığı olan KURUMSAL MARKA ve NET MODELLER üzerinden karşılaştırma yap. 
9. BÜTÇE KORUMA KURALI: Kullanıcı bir bütçe belirttiyse (Örn: ""3000 TL"", ""5 bin TL""), 
   önerdiğin ürünlerin fiyatı bu bütçeyi aşıyorsa cevabın EN BAŞINA şu uyarıyı ekle:
   <div style=""background:rgba(229,62,62,0.1); border:1px solid #e53e3e; border-radius:10px; 
               padding:12px 16px; margin-bottom:16px; color:#fc8181; font-size:13px;"">
   ⚠️ <b>Bütçe Uyarısı:</b> Önerdiğim ürünler belirttiğin bütçeyi aşıyor olabilir. 
   Piyasa koşulları nedeniyle bu bütçeyle seçenekler kısıtlı — 
   sana en yakın fiyatlı modelleri getirdim.
   </div>
   Eğer tüm öneriler bütçe içindeyse bu uyarıyı KOYMA.
ADIM 1 — YANIT FORMATI SEÇİMİ:
                Aşağıdaki 4 durumdan birine uygun formatta SADECE HTML ile yanıt ver!

                -- - DURUM 1: GENEL SORU ---
                Kullanıcının sorusunda ürün tipi / kullanım amacı net değilse 2 - 3 soru sor, ürün önerme:
< p > Harika! Sana en uygun seçeneği bulabilmem için birkaç sorum var:</ p >
- Cinsiyet belirtilmedi (kadın mı, erkek mi, çocuk mu?) — giyim, ayakkabı, çanta için zorunlu sor
< ul >< li > [Sorular] </ li ></ ul >

---DURUM 2: SPESİFİK ÜRÜN(Alınır mı?) ---
< p >< b >🎯 Alınır mı?:</ b > (Tek cümle)</ p >
< p >< b >🔍 Ürün Analizi:</ b > (Artı ve eksiler)</ p >
< p >< b >💡 Alternatif:</ b > (Varsa öner ve temiz HTML link ver)</ p >

---DURUM 3: TAVSİYE İSTEĞİ(2 veya 3 Ürün) ---
< h3 >🎯 Özet Tavsiye</ h3 >< p > (Tek cümle giriş)</ p >
< h3 >📊 Karşılaştırma Tablosu</ h3 >
< table style = ""width: 100 %; border - collapse: collapse; margin - bottom: 20px; text - align: left; "" border = ""1"" >
  < tr style = ""background - color: #f3f4f6;"">
    < th style = ""padding: 12px; border: 1px solid #e5e7eb;"">Kriter</th><th style=""padding: 12px; border: 1px solid #e5e7eb;"">[Ürün 1 Tam Model Adı]</th><th style=""padding: 12px; border: 1px solid #e5e7eb;"">[Ürün 2 Tam Model Adı]</th>
  </ tr >
  < tr >< td style = ""padding: 12px; border: 1px solid #e5e7eb;""><b>Fiyat</b></td><td style=""padding: 12px; border: 1px solid #e5e7eb;"">[Fiyat veya Linke Gidin]</td><td style=""padding: 12px; border: 1px solid #e5e7eb;"">[Fiyat veya Linke Gidin]</td></tr>
  <tr>< td style = ""padding: 12px; border: 1px solid #e5e7eb;""><b>Artısı</b></td><td style=""padding: 12px; border: 1px solid #e5e7eb;"">...</td><td style=""padding: 12px; border: 1px solid #e5e7eb;"">...</td></tr>
  <tr>< td style = ""padding: 12px; border: 1px solid #e5e7eb;""><b>Eksisi</b></td><td style=""padding: 12px; border: 1px solid #e5e7eb;"">...</td><td style=""padding: 12px; border: 1px solid #e5e7eb;"">...</td></tr>
</ table >
< h3 >🛒 Senin İçin Seçtiğim Ürünler</ h3 >
< div style = ""margin - bottom: 16px; "" >
  < b > 1. [GERÇEK MARKA VE MODEL YAZ] </ b >< br >💡 < b > Neden bu ?:</ b > [Açıklama] < br >🗣️ < b > Yorumlar:</ b > [Özet] < br > [LİNKLER BURAYA]
</ div >
< div style = ""margin - bottom: 16px; "" >
  < b > 2. [GERÇEK MARKA VE MODEL YAZ] </ b >< br >💡 < b > Neden bu ?:</ b > [Açıklama] < br >🗣️ < b > Yorumlar:</ b > [Özet] < br > [LİNKLER BURAYA]
</ div >

---DURUM 4: İKİ ÜRÜN KIYASLAMA(VS)-- -
Kullanıcı iki ürün/ link verip kıyaslama isterse.
< h3 >⚔️ Karşılaştırma: [Ürün 1] vs[Ürün 2] </ h3 >
< table style = ""width: 100 %; border - collapse: collapse; margin - bottom: 20px; text - align: left; "" border = ""1"" >
  < tr style = ""background - color: #f3f4f6;""><th style=""padding: 12px; border: 1px solid #e5e7eb;"">Kriter</th><th style=""padding: 12px; border: 1px solid #e5e7eb;"">[Ürün 1]</th><th style=""padding: 12px; border: 1px solid #e5e7eb;"">[Ürün 2]</th></tr>
  < tr >< td style = ""padding: 12px; border: 1px solid #e5e7eb;""><b>Artısı</b></td><td style=""padding: 12px; border: 1px solid #e5e7eb;"">...</td><td style=""padding: 12px; border: 1px solid #e5e7eb;"">...</td></tr>
</ table >
< h3 >🏆 Aura'nın Kararı</h3>
< div style = ""background - color: rgba(59, 130, 246, 0.08); padding: 15px; border - left: 4px solid #3b82f6; border-radius: 4px;"">
  <p>< b > Kimi Seçmelisin ?:</ b > [Karar cümlesi] </ p >
</ div > ";

                using var client = new HttpClient();
                string finalAnswer = null;
                bool isRequestSuccessful = false;

                foreach (var currentKey in _apiKeys)
                {
                    try
                    {
                        string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={currentKey}";
                        var requestBody = new { contents = new[] { new { parts = new[] { new { text = prompt } } } } };
                        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

                        var response = await client.PostAsync(url, content);
                        string responseString = await response.Content.ReadAsStringAsync();

                        if (response.IsSuccessStatusCode)
                        {
                            using (JsonDocument doc = JsonDocument.Parse(responseString))
                            {
                                finalAnswer = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
                            }
                            isRequestSuccessful = true;
                            break;
                        }
                        Debug.WriteLine($"⚠️ API KEY hata verdi: {response.StatusCode}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"🚨 Gemini hata: {ex.Message}");
                    }
                }

                if (!isRequestSuccessful || string.IsNullOrEmpty(finalAnswer))
                {
                    return "<p>Şu anda yoğunluk nedeniyle isteklerinize cevap veremiyorum. Lütfen biraz sonra tekrar deneyin. 🛑</p>";
                }

                _chatHistory += $"Kullanıcı: {userQuery}\nAura: {finalAnswer}\n---\n";
                return finalAnswer;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("--- GENEL HATA: " + ex.Message + " ---");
                return "Ajan bir sorunla karşılaştı: " + ex.Message;
            }
        }

        // =========================================
        // 📄 5. SAYFA İÇERİĞİ ÇEKME (FetchPageContentAsync)
        // =========================================
        public async Task<string> FetchPageContentAsync(string url, string keyword)
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");

                var response = await client.GetStringAsync(url);
                var text = Regex.Replace(response, "<[^>]*>", " ");
                text = Regex.Replace(text, @"\s+", " ").Trim();

                var sentences = text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
                var relevant = sentences.Where(s => keyword.ToLower().Split(' ').Any(w => w.Length > 3 && s.ToLower().Contains(w))).Take(10).ToList();

                if (relevant.Count > 0) return string.Join(". ", relevant);
                return text.Length > 1500 ? text.Substring(0, 1500) : text;
            }
            catch { return ""; }
        }
    }
}