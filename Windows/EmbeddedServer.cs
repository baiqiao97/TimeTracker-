using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;

namespace TimeTracker
{
    public static class EmbeddedServer
    {
        private static HttpListener? _listener;
        private static bool _running;
        private static DatabaseManager? _db;
        private const int MaxRequestBodySize = 1_000_000;
        private const int TokenExpiryDays = 30;
        private static readonly ConcurrentDictionary<string, (int userId, DateTime expires)> _tokens = new();

        private static readonly JsonSerializerOptions _json = new()
            { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };

        public static bool IsRunning => _running;

        public static void Start(DatabaseManager db, int port = 5080)
        {
            if (_running) return;
            _db = db;
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{port}/");
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            _running = true;
            Logger.Info($"EmbeddedServer started on port {port}");
            Task.Run(ListenLoop);
        }

        public static void Stop()
        {
            _running = false;
            _tokens.Clear();
            try { _listener?.Stop(); _listener?.Close(); } catch { }
            _listener = null;
            Logger.Info("EmbeddedServer stopped");
        }

        private static async Task ListenLoop()
        {
            while (_running && _listener != null)
            {
                try
                {
                    var ctx = await _listener.GetContextAsync();
                    _ = HandleRequest(ctx);
                }
                catch (HttpListenerException) { break; }
                catch (Exception ex) { Logger.Error("ListenLoop error", ex); }
            }
        }

        private static async Task HandleRequest(HttpListenerContext ctx)
        {
            try
            {
                var req = ctx.Request;
                var resp = ctx.Response;
                resp.ContentType = "application/json; charset=utf-8";
                resp.AddHeader("X-Content-Type-Options", "nosniff");

                string body;
                if (req.Url!.AbsolutePath == "/api/auth/register" && req.HttpMethod == "POST")
                    body = await HandleRegister(req);
                else if (req.Url!.AbsolutePath == "/api/auth/login" && req.HttpMethod == "POST")
                    body = await HandleLogin(req);
                else if (req.Url!.AbsolutePath == "/api/sync/download")
                    body = HandleSyncDownload(req);
                else if (req.Url!.AbsolutePath == "/api/sync/upload" && req.HttpMethod == "POST")
                    body = await HandleSyncUpload(req);
                else
                    body = JsonSerializer.Serialize(new { service = "TimeTracker Node", version = "2.0" }, _json);

                var buf = Encoding.UTF8.GetBytes(body);
                resp.ContentLength64 = buf.Length;
                await resp.OutputStream.WriteAsync(buf.AsMemory(), CancellationToken.None);
                resp.Close();
            }
            catch (Exception ex) { Logger.Error("HandleRequest error", ex); }
        }

        private static async Task<string> HandleRegister(HttpListenerRequest req)
        {
            var raw = await ReadLimitedBody(req);
            using var doc = JsonDocument.Parse(raw);
            var username = doc.RootElement.GetProperty("username").GetString() ?? "";
            var password = doc.RootElement.GetProperty("password").GetString() ?? "";

            if (username.Length < 6 || password.Length < 6)
                return JsonSerializer.Serialize(new { error = "用户名和密码至少6位" }, _json);

            var queryRow = QueryFirst("SELECT id FROM users WHERE username=@u", ("@u", username));
            if (queryRow != null)
                return JsonSerializer.Serialize(new { error = "用户名已存在" }, _json);

            var token = Guid.NewGuid().ToString("N");
            var expires = DateTime.UtcNow.AddDays(TokenExpiryDays);
            Exec("INSERT OR IGNORE INTO users(username,password,token,expires_at,created_at) VALUES(@u,@p,@t,@e,@c)",
                ("@u", username), ("@p", PasswordHelper.Hash(password)),
                ("@t", token), ("@e", expires.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                ("@c", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));

            var uidRow = QueryFirst("SELECT id FROM users WHERE token=@t", ("@t", token));
            var userId = uidRow != null ? Convert.ToInt32(uidRow["id"], CultureInfo.InvariantCulture) : 0;
            _tokens[token] = (userId, expires);

            Logger.Info($"User registered: {username} (id={userId})");
            return JsonSerializer.Serialize(new { token, expiresAt = expires, userId }, _json);
        }

        private static async Task<string> HandleLogin(HttpListenerRequest req)
        {
            var raw = await ReadLimitedBody(req);
            using var doc = JsonDocument.Parse(raw);
            var username = doc.RootElement.GetProperty("username").GetString() ?? "";
            var password = doc.RootElement.GetProperty("password").GetString() ?? "";

            var row = QueryFirst("SELECT id,password,token,expires_at FROM users WHERE username=@u",
                ("@u", username));
            if (row == null)
                return JsonSerializer.Serialize(new { error = "用户名或密码错误" }, _json);

            if (!PasswordHelper.Verify(password, row["password"]?.ToString() ?? ""))
                return JsonSerializer.Serialize(new { error = "用户名或密码错误" }, _json);

            var token = row["token"]?.ToString();
            var expiresStr = row["expires_at"]?.ToString();

            if (string.IsNullOrEmpty(token) || expiresStr == null ||
                !DateTime.TryParse(expiresStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var expires) || expires < DateTime.UtcNow)
            {
                token = Guid.NewGuid().ToString("N");
                expires = DateTime.UtcNow.AddDays(TokenExpiryDays);
                Exec("UPDATE users SET token=@t,expires_at=@e WHERE id=@id",
                    ("@t", token), ("@e", expires.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                    ("@id", row["id"]!));
            }

            var uid = Convert.ToInt32(row["id"], CultureInfo.InvariantCulture);
            _tokens[token] = (uid, expires);
            Logger.Info($"User logged in: {username} (id={uid})");
            return JsonSerializer.Serialize(new { token, expiresAt = expires, userId = row["id"] }, _json);
        }

        private static string HandleSyncDownload(HttpListenerRequest req)
        {
            var auth = GetAuth(req);
            if (auth == null)
                return JsonSerializer.Serialize(new { error = "未授权" }, _json);

            var since = req.QueryString["since"];
            var records = _db!.GetTimeRecords(
                since != null ? DateTime.Parse(since, CultureInfo.InvariantCulture) : DateTime.MinValue,
                DateTime.MaxValue,
                auth.Value.userId);
            return JsonSerializer.Serialize(records, _json);
        }

        private static async Task<string> HandleSyncUpload(HttpListenerRequest req)
        {
            var auth = GetAuth(req);
            if (auth == null)
                return JsonSerializer.Serialize(new { error = "未授权" }, _json);

            var raw = await ReadLimitedBody(req);
            var records = JsonSerializer.Deserialize<List<TimeRecordData>>(raw, _json);
            int count = 0;
            if (records != null)
            {
                var existing = _db!.GetAllRecordKeys(auth.Value.userId);
                var toAdd = records.Where(r =>
                {
                    if (!DateTime.TryParse(r.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var recordDate)) return false;
                    var key = $"{recordDate:yyyyMMdd}|{r.ProcessName}|{r.DeviceId}";
                    return !existing.Contains(key);
                }).ToList();

                foreach (var r in toAdd) r.UserId = auth.Value.userId;
                if (toAdd.Count > 0) { _db.InsertTimeRecords(toAdd); count = toAdd.Count; }
            }
            return JsonSerializer.Serialize(new { count }, _json);
        }

        private static (int userId, string token)? GetAuth(HttpListenerRequest req)
        {
            var authHeader = req.Headers["Authorization"];
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.Ordinal))
                return null;
            var token = authHeader["Bearer ".Length..].Trim();
            if (_tokens.TryGetValue(token, out var entry))
            {
                if (entry.expires < DateTime.UtcNow)
                {
                    _tokens.TryRemove(token, out _);
                    return null;
                }
                return (entry.userId, token);
            }
            var row = QueryFirst("SELECT id,expires_at FROM users WHERE token=@t", ("@t", token));
            if (row == null) return null;
            var expiresStr = row["expires_at"]?.ToString();
            if (expiresStr != null && DateTime.Parse(expiresStr, CultureInfo.InvariantCulture) < DateTime.UtcNow)
                return null;
            var uid = Convert.ToInt32(row["id"], CultureInfo.InvariantCulture);
            _tokens[token] = (uid, expiresStr != null ? DateTime.Parse(expiresStr, CultureInfo.InvariantCulture) : DateTime.MaxValue);
            return (uid, token);
        }

        private static string DbPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "time_tracker.db");

        private static int Exec(string sql, params (string, object?)[] prms)
        {
            using var c = new System.Data.SQLite.SQLiteConnection($"Data Source={DbPath};Version=3;");
            c.Open();
            // CA2100: 安全 — 调用方传入的 SQL 均为硬编码常量，参数使用 AddWithValue 参数化
#pragma warning disable CA2100
            using var cmd = new System.Data.SQLite.SQLiteCommand(sql, c);
#pragma warning restore CA2100
            foreach (var (k, v) in prms)
                cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
            return cmd.ExecuteNonQuery();
        }

        private static Dictionary<string, object?>? QueryFirst(string sql, params (string, object?)[] prms)
        {
            using var c = new System.Data.SQLite.SQLiteConnection($"Data Source={DbPath};Version=3;");
            c.Open();
            // CA2100: 安全 — 调用方传入的 SQL 均为硬编码常量，参数使用 AddWithValue 参数化
#pragma warning disable CA2100
            using var cmd = new System.Data.SQLite.SQLiteCommand(sql, c);
#pragma warning restore CA2100
            foreach (var (k, v) in prms)
                cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            var d = new Dictionary<string, object?>();
            for (int i = 0; i < r.FieldCount; i++)
                d[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
            return d;
        }

        private static async Task<string> ReadLimitedBody(HttpListenerRequest req)
        {
            if (req.ContentLength64 > MaxRequestBodySize)
                throw new InvalidOperationException("请求体过大");
            using var sr = new StreamReader(req.InputStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096, leaveOpen: true);
            var result = await sr.ReadToEndAsync();
            if (result.Length > MaxRequestBodySize)
                throw new InvalidOperationException("请求体过大");
            return result;
        }
    }
}
