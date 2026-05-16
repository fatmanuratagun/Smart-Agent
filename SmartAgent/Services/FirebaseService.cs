using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartAgent.Services
{
    public class SearchRecord
    {
        [JsonPropertyName("query")]
        public string Query { get; set; } = "";

        [JsonPropertyName("timestamp")]
        public string Timestamp { get; set; } = "";

        [JsonPropertyName("resultCount")]
        public int ResultCount { get; set; }
               

        // Firebase'den gelen unique key — JSON'a yazılmaz
        [JsonIgnore]
        public string FirebaseKey { get; set; } = "";
    }

    public class UserProfile
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("monthlyBudget")]
        public decimal MonthlyBudget { get; set; }

        [JsonPropertyName("updatedAt")]
        public string UpdatedAt { get; set; } = "";
    }
   
    public class FirebaseService
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;

        public FirebaseService(string databaseUrl)
        {
            _http = new HttpClient();
            _baseUrl = databaseUrl;
        }

        // ─── ARAMA GEÇMİŞİ ───────────────────────────────────────
        public class AdviceRecord
        {
            [JsonPropertyName("product")]
            public string Product { get; set; } = "";

            [JsonPropertyName("advice")]
            public string Advice { get; set; } = "";

            [JsonPropertyName("timestamp")]
            public string Timestamp { get; set; } = "";

            [JsonIgnore]
            public string FirebaseKey { get; set; } = "";
        }
        public async Task<List<AdviceRecord>> GetAdvicesAsync(string userId)
        {
            var response = await _http.GetStringAsync(
                $"{_baseUrl}/users/{userId}/advices.json"
            );

            if (response == "null") return new List<AdviceRecord>();

            var dict = JsonSerializer.Deserialize<Dictionary<string, AdviceRecord>>(response);

            if (dict == null) return new List<AdviceRecord>();

            foreach (var kvp in dict)
                kvp.Value.FirebaseKey = kvp.Key;

            return dict.Values
                       .OrderByDescending(a => a.Timestamp)
                       .ToList();
        }
        public async Task SaveSearchAsync(string userId, string query, int resultCount)
        {
            var search = new
            {
                query = query,
                timestamp = DateTime.UtcNow.ToString("o"),
                resultCount = resultCount
            };

            var json = JsonSerializer.Serialize(search);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            await _http.PostAsync(
                $"{_baseUrl}/users/{userId}/searchHistory.json",
                content
            );
        }

        // ─── ARAMA SİLME ─────────────────────────────────────────
        public async Task DeleteSearchAsync(string userId, string searchId)
        {
            await _http.DeleteAsync(
                $"{_baseUrl}/users/{userId}/searchHistory/{searchId}.json"
            );
        }
        public async Task<List<SearchRecord>> GetSearchHistoryAsync(string userId)
        {
            var response = await _http.GetStringAsync(
                $"{_baseUrl}/users/{userId}/searchHistory.json"
            );

            if (response == "null") return new List<SearchRecord>();

            var dict = JsonSerializer.Deserialize<Dictionary<string, SearchRecord>>(response);

            if (dict == null) return new List<SearchRecord>();

            // Key'i (Firebase ID) her kayda ekle
            foreach (var kvp in dict)
            {
                kvp.Value.FirebaseKey = kvp.Key;
            }

            return dict.Values
                       .OrderByDescending(s => s.Timestamp)
                       .ToList();
        }
        public async Task DeleteAllSearchesAsync(string userId)
        {
            await _http.DeleteAsync(
                $"{_baseUrl}/users/{userId}/searchHistory.json"
            );
            await _http.DeleteAsync(
                $"{_baseUrl}/users/{userId}/advices.json"
            );
        }

        // ─── AURA TAVSİYELERİ ────────────────────────────────────

        public async Task SaveAdviceAsync(string userId, string product, string advice)
        {
            var record = new
            {
                product = product,
                advice = advice,
                timestamp = DateTime.UtcNow.ToString("o")
            };

            var json = JsonSerializer.Serialize(record);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            await _http.PostAsync(
                $"{_baseUrl}/users/{userId}/advices.json",
                content
            );
        }

        // ─── UYARILAR ────────────────────────────────────────────

        public async Task SaveWarningAsync(string userId, string product,
                                           string warningType, string severity)
        {
            var warning = new
            {
                type = warningType,
                product = product,
                severity = severity,
                timestamp = DateTime.UtcNow.ToString("o")
            };

            var json = JsonSerializer.Serialize(warning);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            await _http.PostAsync(
                $"{_baseUrl}/users/{userId}/warnings.json",
                content
            );
        }

        // ─── KULLANICI PROFİLİ ───────────────────────────────────

        public async Task SaveUserProfileAsync(string userId, string name, decimal monthlyBudget)
        {
            var profile = new
            {
                name = name,
                monthlyBudget = monthlyBudget,
                updatedAt = DateTime.UtcNow.ToString("o")
            };

            var json = JsonSerializer.Serialize(profile);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            await _http.PutAsync(
                $"{_baseUrl}/users/{userId}/profile.json",
                content
            );
        }

        public async Task<UserProfile?> GetUserProfileAsync(string userId)
        {
            var response = await _http.GetStringAsync(
                $"{_baseUrl}/users/{userId}/profile.json"
            );

            if (response == "null") return null;
            return JsonSerializer.Deserialize<UserProfile>(response);
        }
    }
}