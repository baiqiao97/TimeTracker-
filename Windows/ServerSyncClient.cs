using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TimeTracker
{
    public static class ServerSyncClient
    {
        private static readonly HttpClient _client = new(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(10) };
        private static readonly JsonSerializerOptions _jsonOpts = new()
            { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };

        public static string DefaultUrl => $"http://localhost:{AppSettings.ServerPort}";
        private static string ServerUrl => string.IsNullOrWhiteSpace(AppSettings.ServerUrl)
            ? DefaultUrl : AppSettings.ServerUrl.TrimEnd('/');

        public static async Task<(bool ok, string? error)> RegisterAsync(string username, string password)
        {
            try
            {
                var json = JsonSerializer.Serialize(new { username, password }, _jsonOpts);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                var resp = await _client.PostAsync($"{ServerUrl}/api/auth/register", content);
            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (!resp.IsSuccessStatusCode || root.TryGetProperty("error", out var errProp))
            {
                var err = errProp.GetString() ?? "注册失败";
                return (false, err);
            }

            var token = root.TryGetProperty("token", out var t) ? t.GetString() : "";
            if (string.IsNullOrEmpty(token))
            {
                return (false, "注册响应缺少 token");
            }

            AppSettings.AuthToken = token;
            AppSettings.Save();
            return (true, null);
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        public static async Task<(bool ok, string? error)> LoginAsync(string username, string password)
        {
            try
            {
                var json = JsonSerializer.Serialize(new { username, password }, _jsonOpts);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                var resp = await _client.PostAsync($"{ServerUrl}/api/auth/login", content);
                var body = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (!resp.IsSuccessStatusCode || root.TryGetProperty("error", out var errProp))
                {
                    return (false, errProp.GetString());
                }

                var token = root.TryGetProperty("token", out var t) ? t.GetString() : "";

                if (string.IsNullOrEmpty(token))
                {
                    return (false, "登录响应缺少 token");
                }

                AppSettings.AuthToken = token;
                AppSettings.LastSyncTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                AppSettings.Save();

                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public static async Task<(bool ok, string? err)> SyncAsync(DatabaseManager db)
        {
            if (string.IsNullOrEmpty(AppSettings.AuthToken)) return (false, null);

            try
            {
                _client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", AppSettings.AuthToken);

                var since = AppSettings.LastSyncTime > DateTime.MinValue
                    ? AppSettings.LastSyncTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : null;
                var dlUrl = $"{ServerUrl}/api/sync/download?limit=10000";
                if (since != null) dlUrl += $"&since={Uri.EscapeDataString(since)}";

                // 1. 下载服务器记录
                var resp = await _client.GetStringAsync(dlUrl);
                var records = JsonSerializer.Deserialize<List<TimeRecordData>>(resp, _jsonOpts);
                if (records != null && records.Count > 0)
                {
                    var existing = db.GetAllRecordKeys();
                    var toAdd = new List<TimeRecordData>();
                    foreach (var r in records)
                    {
                        if (DateTime.TryParse(r.Date, out var recordDate))
                        {
                            var key = $"{recordDate:yyyyMMdd}|{r.ProcessName}|{r.DeviceId}";
                            if (!existing.Contains(key))
                                toAdd.Add(r);
                        }
                    }
                    if (toAdd.Count > 0) db.InsertTimeRecords(toAdd);
                }

                // 2. 上传本机记录
                var local = db.GetTimeRecords(AppSettings.LastSyncTime, DateTime.MaxValue);
                var myRecords = local.Where(r => r.DeviceId.Contains(Environment.MachineName, StringComparison.Ordinal)).ToList();
                if (myRecords.Count > 0)
                {
                    var json = JsonSerializer.Serialize(myRecords, _jsonOpts);
                    using var content = new StringContent(json, Encoding.UTF8, "application/json");
                    await _client.PostAsync($"{ServerUrl}/api/sync/upload", content);
                }

                AppSettings.LastSyncTime = DateTime.Now;
                AppSettings.Save();
                return (true, null);
            }
            catch (Exception ex) { Logger.Error("Sync error", ex); return (false, null); }
        }
    }
}
