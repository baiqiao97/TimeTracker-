using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TimeTracker
{
    public static class AIService
    {
        private static readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(30) };
        private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public enum Provider { OpenAI, Anthropic }

        public static bool IsConfigured => !string.IsNullOrEmpty(AppSettings.AiApiKey);

        public static async Task<string[]?> DecomposeGoalAsync(string goal, string? apiKey = null)
        {
            var key = apiKey ?? AppSettings.AiApiKey;
            if (string.IsNullOrEmpty(key)) return null;
            var provider = AppSettings.AiProvider;

            var prompt = "You are a goal management expert. Break down this goal into 3-5 measurable phases:\n" +
                "\"" + goal + "\"\n" +
                "Each phase needs: title (max 10 chars), description (max 30 chars), estimated minutes (integer).\n" +
                "Return ONLY a JSON array: [{\"title\":\"...\",\"desc\":\"...\",\"minutes\":120}]";

            var response = await CallAI(prompt, key, provider);
            if (response == null) return null;

            try
            {
                var start = response.IndexOf('[');
                var end = response.LastIndexOf(']');
                if (start < 0 || end < 0) return new[] { response.Trim() };
                var json = response[start..(end + 1)];
                var phases = JsonSerializer.Deserialize<List<PhaseSuggestion>>(json, _jsonOpts);
                return phases?.Select(p => p.Title + "|" + p.Desc + "|" + p.Minutes).ToArray()
                    ?? new[] { response.Trim() };
            }
            catch { return new[] { response.Trim() }; }
        }

        public static async Task<string?> EvaluatePhaseAsync(string goal, string phaseTitle,
            int planMinutes, int actualMinutes, double effectiveRatio, string? apiKey = null)
        {
            var key = apiKey ?? AppSettings.AiApiKey;
            if (string.IsNullOrEmpty(key)) return null;
            var provider = AppSettings.AiProvider;

            var prompt = "Goal: " + goal + "\nPhase: " + phaseTitle +
                "\nPlanned: " + planMinutes + "min, Actual: " + actualMinutes + "min, Efficiency: " + effectiveRatio.ToString("P0") +
                "\nGive a brief evaluation and encouragement in 80 chars or less.";

            return await CallAI(prompt, key, provider);
        }

        private static async Task<string?> CallAI(string prompt, string apiKey, Provider provider)
        {
            try
            {
                if (provider == Provider.Anthropic)
                {
                    _client.DefaultRequestHeaders.Clear();
                    _client.DefaultRequestHeaders.Add("x-api-key", apiKey);
                    _client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

                    var body = JsonSerializer.Serialize(new
                    {
                        model = "claude-3-haiku-20240307",
                        max_tokens = 500,
                        messages = new[] { new { role = "user", content = prompt } }
                    }, _jsonOpts);

                    var resp = await _client.PostAsync("https://api.anthropic.com/v1/messages",
                        new StringContent(body, Encoding.UTF8, "application/json"));
                    var json = await resp.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    return doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString();
                }
                else
                {
                    _client.DefaultRequestHeaders.Clear();
                    _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                    var body = JsonSerializer.Serialize(new
                    {
                        model = "gpt-4o-mini",
                        max_tokens = 500,
                        messages = new[] { new { role = "user", content = prompt } }
                    }, _jsonOpts);

                    var resp = await _client.PostAsync("https://api.openai.com/v1/chat/completions",
                        new StringContent(body, Encoding.UTF8, "application/json"));
                    var json = await resp.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("AI call failed: " + ex.Message);
                return null;
            }
        }

        private class PhaseSuggestion
        {
            public string Title { get; set; } = "";
            public string Desc { get; set; } = "";
            public int Minutes { get; set; }
        }
    }
}
