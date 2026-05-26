using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TimeTracker
{
    public static class EmbeddedServer
    {
        private static HttpListener? _listener;
        private static bool _running;
        private static DatabaseManager? _db;
        private const int MaxRequestBodySize = 1_000_000; // 1MB 限制
        private static readonly Dictionary<string, (int userId, DateTime expires)> _tokens = [];

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
            Task.Run(ListenLoop);
        }

        public static void Stop()
        {
            _running = false;
            _tokens.Clear();
            try { _listener?.Stop(); _listener?.Close(); } catch { }
            _listener = null;
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
                catch { }
            }
        }

        // ========================== 路由处理 ==========================

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
                {
                    body = await HandleRegister(req);
                }
                else if (req.Url!.AbsolutePath == "/api/auth/login" && req.HttpMethod == "POST")
                {
                    body = await HandleLogin(req);
                }
                else if (req.Url!.AbsolutePath == "/api/sync/download")
                {
                    body = HandleSyncDownload(req);
                }
                else if (req.Url!.AbsolutePath == "/api/sync/upload" && req.HttpMethod == "POST")
                {
                    body = await HandleSyncUpload(req);
                }
                else
                {
                    body = JsonSerializer.Serialize(new { service = "TimeTracker Node", version = "2.0" }, _json);
                }

                var buf = Encoding.UTF8.GetBytes(body);
                resp.ContentLength64 = buf.Length;
                await resp.OutputStream.WriteAsync(buf.AsMemory(), CancellationToken.None);
                resp.Close();
            }
            catch { }
        }

        // ========================== 认证端点 ==========================

        private static async Task<string> HandleRegister(HttpListenerRequest req)
        {
            var raw = await ReadLimitedBody(req);
            using var doc = JsonDocument.Parse(raw);
            var username = doc.RootElement.GetProperty("username").GetString() ?? "";
            var password = doc.RootElement.GetProperty("password").GetString() ?? "";

            if (username.Length < 6 || password.Length < 6)
                return JsonSerializer.Serialize(new { error = "用户名和密码至少6位" }, _json);

            // 检查用户名是否已存在
            var queryRow = QueryFirst("SELECT id FROM users WHERE username=@u", ("@u", username));
            if (queryRow != null)
                return JsonSerializer.Serialize(new { error = "用户名已存在" }, _json);

            var token = Guid.NewGuid().ToString("N");
            var expires = DateTime.UtcNow.AddDays(30);
            Exec("INSERT OR IGNORE INTO users(username,password,token,expires_at,created_at) VALUES(@u,@p,@t,@e,@c)",
                ("@u", username), ("@p", HashPwd(password)),
                ("@t", token), ("@e", expires.ToString("yyyy-MM-dd HH:mm:ss")),
                ("@c", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")));

            var uidRow = QueryFirst("SELECT id FROM users WHERE token=@t", ("@t", token));
            var userId = uidRow != null ? Convert.ToInt32(uidRow["id"]) : 0;
            _tokens[token] = (userId, expires);

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

            if (!VerifyPwd(password, row["password"]?.ToString() ?? ""))
                return JsonSerializer.Serialize(new { error = "用户名或密码错误" }, _json);

            var token = row["token"]?.ToString();
            var expiresStr = row["expires_at"]?.ToString();

            if (string.IsNullOrEmpty(token) || expiresStr == null ||
                !DateTime.TryParse(expiresStr, out var expires) || expires < DateTime.UtcNow)
            {
                token = Guid.NewGuid().ToString("N");
                expires = DateTime.UtcNow.AddDays(30);
                Exec("UPDATE users SET token=@t,expires_at=@e WHERE id=@id",
                    ("@t", token), ("@e", expires.ToString("yyyy-MM-dd HH:mm:ss")),
                    ("@id", row["id"]!));
            }

            _tokens[token] = (Convert.ToInt32(row["id"]), expires);
            return JsonSerializer.Serialize(new { token, expiresAt = expires, userId = row["id"] }, _json);
        }

        // ========================== 同步端点 ==========================

        private static string HandleSyncDownload(HttpListenerRequest req)
        {
            var auth = GetAuth(req);
            if (auth == null)
                return JsonSerializer.Serialize(new { error = "未授权" }, _json);

            var since = req.QueryString["since"];
            var records = _db!.GetTimeRecords(
                since != null ? DateTime.Parse(since) : DateTime.MinValue, DateTime.MaxValue);
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
                var existing = _db!.GetAllRecordKeys();
                var toAdd = records.Where(r => !existing.Contains(
                    $"{r.Date}|{r.ProcessName}|{r.DeviceId}")).ToList();
                if (toAdd.Count > 0) { _db.InsertTimeRecords(toAdd); count = toAdd.Count; }
            }
            return JsonSerializer.Serialize(new { count }, _json);
        }

        // ========================== 认证验证 ==========================

        private static (int userId, string token)? GetAuth(HttpListenerRequest req)
        {
            var authHeader = req.Headers["Authorization"];
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                return null;
            var token = authHeader["Bearer ".Length..].Trim();
            if (_tokens.TryGetValue(token, out var entry))
            {
                if (entry.expires < DateTime.UtcNow)
                {
                    _tokens.Remove(token);
                    return null;
                }
                return (entry.userId, token);
            }
            // 回退到数据库查询
            var row = QueryFirst("SELECT id,expires_at FROM users WHERE token=@t", ("@t", token));
            if (row == null) return null;
            var expiresStr = row["expires_at"]?.ToString();
            if (expiresStr != null && DateTime.Parse(expiresStr) < DateTime.UtcNow)
                return null;
            var uid = Convert.ToInt32(row["id"]);
            _tokens[token] = (uid, expiresStr != null ? DateTime.Parse(expiresStr) : DateTime.MaxValue);
            return (uid, token);
        }

        // ========================== 密码哈希 ==========================

        private static string HashPwd(string pwd)
        {
            var bytes = Encoding.UTF8.GetBytes(pwd);
            var salt = RandomNumberGenerator.GetBytes(16);
            var hash = Rfc2898DeriveBytes.Pbkdf2(bytes, salt, 100_000, HashAlgorithmName.SHA256, 32);
            return $"{Convert.ToHexString(salt)}:{Convert.ToHexString(hash)}";
        }

        private static bool VerifyPwd(string pwd, string stored)
        {
            var parts = stored.Split(':');
            if (parts.Length != 2) return false;
            try
            {
                var salt = Convert.FromHexString(parts[0]);
                var expected = Convert.FromHexString(parts[1]);
                var actual = Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.UTF8.GetBytes(pwd), salt, 100_000, HashAlgorithmName.SHA256, 32);
                return CryptographicOperations.FixedTimeEquals(expected, actual);
            }
            catch { return false; }
        }

        // ========================== DB 辅助 ==========================

        private static string DbPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "time_tracker.db");

        private static int Exec(string sql, params (string, object?)[] prms)
        {
            using var c = new System.Data.SQLite.SQLiteConnection($"Data Source={DbPath};Version=3;");
            c.Open();
            using var cmd = new System.Data.SQLite.SQLiteCommand(sql, c);
            foreach (var (k, v) in prms)
                cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
            return cmd.ExecuteNonQuery();
        }

        private static Dictionary<string, object?>? QueryFirst(string sql, params (string, object?)[] prms)
        {
            using var c = new System.Data.SQLite.SQLiteConnection($"Data Source={DbPath};Version=3;");
            c.Open();
            using var cmd = new System.Data.SQLite.SQLiteCommand(sql, c);
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
            return await sr.ReadToEndAsync();
        }
    }
}
