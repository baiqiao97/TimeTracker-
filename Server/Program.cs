using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:5080");
var app = builder.Build();

var dbPath = Path.Combine(AppContext.BaseDirectory, "timetracker.db");
InitDb(dbPath);

// ===== 认证 =====

app.MapPost("/api/auth/register", (AuthReq req) =>
{
    if (string.IsNullOrWhiteSpace(req.Username) || req.Username.Length < 6
        || string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 6)
        return Results.BadRequest(new { error = "用户名和密码至少6位" });
    var existing = QueryFirst(dbPath, "SELECT id FROM users WHERE username=@u", ("@u", req.Username));
    if (existing != null)
        return Results.Conflict(new { error = "用户名已存在" });

    var token = Guid.NewGuid().ToString("N");
    var expires = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd HH:mm:ss");
    Exec(dbPath, "INSERT INTO users(username,password,token,expires_at,created_at) VALUES(@u,@p,@t,@e,@c)",
        ("@u", req.Username), ("@p", Hash(req.Password)),
        ("@t", token), ("@e", expires), ("@c", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")));
    var uid = QueryFirst(dbPath, "SELECT id FROM users WHERE token=@t", ("@t", token));
    return Results.Ok(new { token, expiresAt = expires, userId = uid?["id"] ?? 0 });
});

app.MapPost("/api/auth/login", (AuthReq req) =>
{
    var row = QueryFirst(dbPath, "SELECT id,password,token,expires_at FROM users WHERE username=@u",
        ("@u", req.Username));
    if (row == null) return Results.Unauthorized();
    if (!VerifyHash(req.Password, row["password"]?.ToString() ?? ""))
        return Results.Unauthorized();
    // 若 token 已过期则重新生成
    var token = row["token"]?.ToString();
    var expiresStr = row["expires_at"]?.ToString();
    if (string.IsNullOrEmpty(token) || (expiresStr != null && DateTime.Parse(expiresStr) < DateTime.UtcNow))
    {
        token = Guid.NewGuid().ToString("N");
        Exec(dbPath, "UPDATE users SET token=@t,expires_at=@e WHERE id=@id",
            ("@t", token), ("@e", DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd HH:mm:ss")),
            ("@id", row["id"]!));
    }
    return Results.Ok(new { token, userId = row["id"] });
});

// ===== 数据同步（需认证） =====

app.MapGet("/api/sync/download", (HttpRequest req) =>
{
    var auth = GetAuth(dbPath, req);
    if (auth == null) return Results.Unauthorized();
    var since = req.Query["since"].FirstOrDefault();
    var sql = "SELECT id,process_name,window_title,usage_time,date,device_id,category_id,is_foreground,activity_id FROM time_records WHERE user_id=@uid";
    var prms = new List<(string, object?)> { ("@uid", auth.Value.userId) };
    if (!string.IsNullOrEmpty(since)) { sql += " AND date > @s"; prms.Add(("@s", since)); }
    sql += " ORDER BY date,id LIMIT 10000";
    return Results.Ok(Query(dbPath, sql, prms));
});

app.MapPost("/api/sync/upload", (HttpRequest req, List<RecordDto> records) =>
{
    var auth = GetAuth(dbPath, req);
    if (auth == null) return Results.Unauthorized();
    int count = 0;
    foreach (var r in records)
    {
        Exec(dbPath,
            @"INSERT OR IGNORE INTO time_records(id,process_name,window_title,usage_time,date,device_id,category_id,is_foreground,activity_id,user_id)
              VALUES(@id,@pn,@wt,@ut,@d,@di,@ci,@fg,@ai,@uid)",
            ("@id", r.Id), ("@pn", r.ProcessName), ("@wt", r.WindowTitle),
            ("@ut", r.UsageTime), ("@d", r.Date), ("@di", r.DeviceId),
            ("@ci", r.CategoryId), ("@fg", r.IsForeground ? 1 : 0),
            ("@ai", r.ActivityId), ("@uid", auth.Value.userId));
        count++;
    }
    return Results.Ok(new { count });
});

app.MapGet("/", () => Results.Ok(new { service = "TimeTracker Cloud", version = "2.0" }));

app.Run();

// ===== Helpers =====

static void InitDb(string path)
{
    using var c = new SqliteConnection($"Data Source={path}");
    c.Open();
    foreach (var sql in new[] {
        "PRAGMA journal_mode=WAL",
        "CREATE TABLE IF NOT EXISTS users(id INTEGER PRIMARY KEY AUTOINCREMENT,username TEXT UNIQUE NOT NULL,password TEXT NOT NULL,token TEXT,expires_at TEXT,created_at TEXT)",
        "CREATE TABLE IF NOT EXISTS time_records(id INTEGER NOT NULL,process_name TEXT NOT NULL,window_title TEXT,usage_time INTEGER NOT NULL,date TEXT NOT NULL,device_id TEXT NOT NULL,category_id INTEGER,is_foreground INTEGER DEFAULT 1,activity_id INTEGER,user_id INTEGER NOT NULL,PRIMARY KEY(id,user_id))" })
    {
        using var cmd = new SqliteCommand(sql, c);
        cmd.ExecuteNonQuery();
    }
}

static (int userId, string token)? GetAuth(string path, HttpRequest req)
{
    var token = req.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "");
    if (string.IsNullOrEmpty(token)) return null;
    var row = QueryFirst(path, "SELECT id,expires_at FROM users WHERE token=@t", ("@t", token));
    if (row == null) return null;
    var expiresStr = row["expires_at"]?.ToString();
    if (expiresStr != null && DateTime.Parse(expiresStr) < DateTime.UtcNow)
        return null; // token 已过期
    return (Convert.ToInt32(row["id"]), token);
}

static string Hash(string? pwd)
{
    var bytes = Encoding.UTF8.GetBytes(pwd ?? "");
    var salt = RandomNumberGenerator.GetBytes(16); // 16字节随机盐
    var hash = Rfc2898DeriveBytes.Pbkdf2(bytes, salt, 100_000, HashAlgorithmName.SHA256, 32);
    return $"{Convert.ToHexString(salt)}:{Convert.ToHexString(hash)}";
}

static bool VerifyHash(string? pwd, string stored)
{
    var parts = stored.Split(':');
    if (parts.Length != 2) return false;
    var salt = Convert.FromHexString(parts[0]);
    var expected = Convert.FromHexString(parts[1]);
    var actual = Rfc2898DeriveBytes.Pbkdf2(
        Encoding.UTF8.GetBytes(pwd ?? ""), salt, 100_000, HashAlgorithmName.SHA256, 32);
    return CryptographicOperations.FixedTimeEquals(expected, actual);
}

static int Exec(string path, string sql, params (string, object?)[] prms)
{
    using var c = new SqliteConnection($"Data Source={path}"); c.Open();
    using var cmd = new SqliteCommand(sql, c);
    foreach (var (k, v) in prms) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
    return cmd.ExecuteNonQuery();
}

static List<Dictionary<string, object?>> Query(string path, string sql, IEnumerable<(string, object?)> prms)
{
    var list = new List<Dictionary<string, object?>>();
    using var c = new SqliteConnection($"Data Source={path}"); c.Open();
    using var cmd = new SqliteCommand(sql, c);
    foreach (var (k, v) in prms) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
    using var r = cmd.ExecuteReader();
    while (r.Read()) { var d = new Dictionary<string, object?>(); for (int i = 0; i < r.FieldCount; i++) d[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i); list.Add(d); }
    return list;
}

static Dictionary<string, object?>? QueryFirst(string path, string sql, params (string, object?)[] prms)
    => Query(path, sql, prms).FirstOrDefault();

record AuthReq(string Username, string Password);

record RecordDto
{
    public int Id { get; init; }
    public string ProcessName { get; init; } = "";
    public string? WindowTitle { get; init; }
    public long UsageTime { get; init; }
    public string Date { get; init; } = "";
    public string DeviceId { get; init; } = "";
    public int? CategoryId { get; init; }
    public bool IsForeground { get; init; } = true;
    public int? ActivityId { get; init; }
}
