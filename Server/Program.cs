using Microsoft.Data.Sqlite;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:5080");
var app = builder.Build();

var dbPath = Path.Combine(AppContext.BaseDirectory, "timetracker.db");
InitDb(dbPath);

// ===== 常量 =====
const int TokenExpiryDays = 30;
const int MinPasswordLength = 6;
const int MaxSyncLimit = 10000;
const int RateLimitPerMinute = 30;
const int AuthRateLimitPerMinute = 6;

// ===== 限流 =====
// 修复：定期清理过期键，防止内存无限增长
var rateLimitStore = new ConcurrentDictionary<string, (int count, DateTime window)>();
_ = Task.Run(async () =>
{
    while (true)
    {
        await Task.Delay(TimeSpan.FromMinutes(2));
        var now = DateTime.UtcNow;
// 清理过期限制（线程安全）
        var expiredKeys = rateLimitStore.Where(kvp => kvp.Value.window < now).Select(kvp => kvp.Key).ToArray();
        foreach (var key in expiredKeys)
            rateLimitStore.TryRemove(key, out _);
        }
    }
});

string? CheckRateLimit(HttpRequest req, int maxPerMinute)
{
    var ip = req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var key = $"{ip}:{req.Path}";
    var now = DateTime.UtcNow;

    var entry = rateLimitStore.AddOrUpdate(key,
        _ => (1, now.AddMinutes(1)),
        (_, existing) =>
        {
            if (existing.window < now)
                return (1, now.AddMinutes(1));
            return (existing.count + 1, existing.window);
        });

    if (entry.count > maxPerMinute)
        return JsonSerializer.Serialize(new { error = "请求频率过高，请稍后再试" });

    return null;
}

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";
    int limit = path.StartsWith("/api/auth", StringComparison.Ordinal) ? AuthRateLimitPerMinute : RateLimitPerMinute;
    var rateError = CheckRateLimit(context.Request, limit);
    if (rateError != null)
    {
        context.Response.StatusCode = 429;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(rateError);
        return;
    }
    await next();
});

// ===== 认证 =====

app.MapPost("/api/auth/register", (AuthReq req) =>
{
    if (string.IsNullOrWhiteSpace(req.Username) || req.Username.Length < MinPasswordLength
        || string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < MinPasswordLength)
        return Results.BadRequest(new { error = $"用户名和密码至少{MinPasswordLength}位" });
    var existing = QueryFirst(dbPath, "SELECT id FROM users WHERE username=@u", ("@u", req.Username));
    if (existing != null)
        return Results.Conflict(new { error = "用户名已存在" });

    var token = Guid.NewGuid().ToString("N");
    var expires = DateTime.UtcNow.AddDays(TokenExpiryDays).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    Exec(dbPath, "INSERT INTO users(username,password,token,expires_at,created_at) VALUES(@u,@p,@t,@e,@c)",
        ("@u", req.Username), ("@p", Hash(req.Password)),
        ("@t", token), ("@e", expires), ("@c", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
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
    var token = row["token"]?.ToString();
    var expiresStr = row["expires_at"]?.ToString();
    if (string.IsNullOrEmpty(token) || (expiresStr != null && DateTime.Parse(expiresStr, CultureInfo.InvariantCulture) < DateTime.UtcNow))
    {
        token = Guid.NewGuid().ToString("N");
        Exec(dbPath, "UPDATE users SET token=@t,expires_at=@e WHERE id=@id",
            ("@t", token), ("@e", DateTime.UtcNow.AddDays(TokenExpiryDays).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
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
    // MaxSyncLimit 为 const int，安全拼接
    sql += $" ORDER BY date,id LIMIT {MaxSyncLimit}";
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

app.MapPost("/api/sync/todos/upload", (HttpRequest req, List<TodoDto> todos) =>
{
    var auth = GetAuth(dbPath, req);
    if (auth == null) return Results.Unauthorized();
    int count = 0;
    foreach (var t in todos)
    {
        Exec(dbPath,
            @"INSERT OR IGNORE INTO todo_items(id,title,description,is_completed,priority,due_date,created_at,completed_at,device_id,user_id)
              VALUES(@id,@ti,@d,@ic,@p,@dd,@ca,@co,@di,@uid)",
            ("@id", t.Id), ("@ti", t.Title), ("@d", t.Description),
            ("@ic", t.IsCompleted ? 1 : 0), ("@p", t.Priority),
            ("@dd", t.DueDate), ("@ca", t.CreatedAt), ("@co", t.CompletedAt),
            ("@di", t.DeviceId), ("@uid", auth.Value.userId));
        count++;
    }
    return Results.Ok(new { count });
});

app.MapGet("/api/sync/todos/download", (HttpRequest req) =>
{
    var auth = GetAuth(dbPath, req);
    if (auth == null) return Results.Unauthorized();
    var since = req.Query["since"].FirstOrDefault();
    var sql = "SELECT id,title,description,is_completed,priority,due_date,created_at,completed_at,device_id FROM todo_items WHERE user_id=@uid";
    var prms = new List<(string, object?)> { ("@uid", auth.Value.userId) };
    if (!string.IsNullOrEmpty(since)) { sql += " AND created_at > @s"; prms.Add(("@s", since)); }
    sql += " ORDER BY id LIMIT " + MaxSyncLimit.ToString(CultureInfo.InvariantCulture);
    return Results.Ok(Query(dbPath, sql, prms));
});

app.MapPost("/api/sync/schedules/upload", (HttpRequest req, List<ScheduleDto> schedules) =>
{
    var auth = GetAuth(dbPath, req);
    if (auth == null) return Results.Unauthorized();
    int count = 0;
    foreach (var s in schedules)
    {
        Exec(dbPath,
            @"INSERT OR IGNORE INTO schedules(id,title,description,start_time,end_time,is_all_day,color,created_at,device_id,user_id)
              VALUES(@id,@ti,@d,@st,@et,@ia,@c,@ca,@di,@uid)",
            ("@id", s.Id), ("@ti", s.Title), ("@d", s.Description),
            ("@st", s.StartTime), ("@et", s.EndTime),
            ("@ia", s.IsAllDay ? 1 : 0), ("@c", s.Color),
            ("@ca", s.CreatedAt), ("@di", s.DeviceId), ("@uid", auth.Value.userId));
        count++;
    }
    return Results.Ok(new { count });
});

app.MapGet("/api/sync/schedules/download", (HttpRequest req) =>
{
    var auth = GetAuth(dbPath, req);
    if (auth == null) return Results.Unauthorized();
    var since = req.Query["since"].FirstOrDefault();
    var sql = "SELECT id,title,description,start_time,end_time,is_all_day,color,created_at,device_id FROM schedules WHERE user_id=@uid";
    var prms = new List<(string, object?)> { ("@uid", auth.Value.userId) };
    if (!string.IsNullOrEmpty(since)) { sql += " AND created_at > @s"; prms.Add(("@s", since)); }
    sql += " ORDER BY start_time,id LIMIT " + MaxSyncLimit.ToString(CultureInfo.InvariantCulture);
    return Results.Ok(Query(dbPath, sql, prms));
});

app.MapGet("/", () => Results.Ok(new { service = "TimeTracker Cloud", version = "3.0" }));

app.Run();

// ===== Helpers =====

static void InitDb(string path)
{
    using var c = new SqliteConnection($"Data Source={path}");
    c.Open();
    foreach (var sql in new[] {
        // CA2100: 安全 — 均为硬编码 DDL 常量
#pragma warning disable CA2100
        "PRAGMA journal_mode=WAL",
        "CREATE TABLE IF NOT EXISTS users(id INTEGER PRIMARY KEY AUTOINCREMENT,username TEXT UNIQUE NOT NULL,password TEXT NOT NULL,token TEXT,expires_at TEXT,created_at TEXT)",
        "CREATE TABLE IF NOT EXISTS time_records(id INTEGER NOT NULL,process_name TEXT NOT NULL,window_title TEXT,usage_time INTEGER NOT NULL,date TEXT NOT NULL,device_id TEXT NOT NULL,category_id INTEGER,is_foreground INTEGER DEFAULT 1,activity_id INTEGER,user_id INTEGER NOT NULL,PRIMARY KEY(id,user_id))",
        "CREATE TABLE IF NOT EXISTS manual_records(id INTEGER PRIMARY KEY AUTOINCREMENT,title TEXT NOT NULL,description TEXT DEFAULT '',start_time TEXT NOT NULL,duration_minutes INTEGER DEFAULT 0,category_id INTEGER DEFAULT NULL,activity_id INTEGER DEFAULT NULL,user_id INTEGER DEFAULT NULL,created_at TEXT NOT NULL)",
        "CREATE TABLE IF NOT EXISTS todo_items(id INTEGER NOT NULL,title TEXT NOT NULL,description TEXT DEFAULT '',is_completed INTEGER DEFAULT 0,priority INTEGER DEFAULT 1,due_date TEXT,created_at TEXT NOT NULL,completed_at TEXT,device_id TEXT NOT NULL,user_id INTEGER NOT NULL,PRIMARY KEY(id,user_id))",
        "CREATE TABLE IF NOT EXISTS schedules(id INTEGER NOT NULL,title TEXT NOT NULL,description TEXT DEFAULT '',start_time TEXT NOT NULL,end_time TEXT,is_all_day INTEGER DEFAULT 0,color TEXT DEFAULT '#6c5ce7',created_at TEXT NOT NULL,device_id TEXT NOT NULL,user_id INTEGER NOT NULL,PRIMARY KEY(id,user_id))"
#pragma warning restore CA2100
    })
    {
        using var cmd = new SqliteCommand(sql, c);
        cmd.ExecuteNonQuery();
    }
}

static (int userId, string token)? GetAuth(string path, HttpRequest req)
{
    var token = req.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.Ordinal);
    if (string.IsNullOrEmpty(token)) return null;
    var row = QueryFirst(path, "SELECT id,expires_at FROM users WHERE token=@t", ("@t", token));
    if (row == null) return null;
    var expiresStr = row["expires_at"]?.ToString();
    if (expiresStr != null && DateTime.Parse(expiresStr, CultureInfo.InvariantCulture) < DateTime.UtcNow)
        return null;
    return (Convert.ToInt32(row["id"], CultureInfo.InvariantCulture), token);
}

const int SaltSize = 16;
const int HashSize = 32;
const int Iterations = 100_000;

static string Hash(string? pwd)
{
    var bytes = Encoding.UTF8.GetBytes(pwd ?? "");
    var salt = RandomNumberGenerator.GetBytes(SaltSize);
    var hash = Rfc2898DeriveBytes.Pbkdf2(bytes, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
    return $"{Convert.ToHexString(salt)}:{Convert.ToHexString(hash)}";
}

static bool VerifyHash(string? pwd, string stored)
{
    var parts = stored.Split(':');
    if (parts.Length != 2) return false;
    try
    {
        var salt = Convert.FromHexString(parts[0]);
        var expected = Convert.FromHexString(parts[1]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(pwd ?? ""), salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
    catch { return false; }
}

static int Exec(string path, string sql, params (string, object?)[] prms)
{
    using var c = new SqliteConnection($"Data Source={path}"); c.Open();
    // CA2100: 安全 — sql 参数均通过调用方传入，实际调用处均为硬编码 SQL，参数使用 @k 参数化
#pragma warning disable CA2100
    using var cmd = new SqliteCommand(sql, c);
#pragma warning restore CA2100
    foreach (var (k, v) in prms) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
    return cmd.ExecuteNonQuery();
}

static List<Dictionary<string, object?>> Query(string path, string sql, IEnumerable<(string, object?)> prms)
{
    var list = new List<Dictionary<string, object?>>();
    using var c = new SqliteConnection($"Data Source={path}"); c.Open();
    // CA2100: 安全 — 同上
#pragma warning disable CA2100
    using var cmd = new SqliteCommand(sql, c);
#pragma warning restore CA2100
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

record TodoDto
{
    public int Id { get; init; }
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public bool IsCompleted { get; init; }
    public int Priority { get; init; } = 1;
    public string? DueDate { get; init; }
    public string CreatedAt { get; init; } = "";
    public string? CompletedAt { get; init; }
    public string DeviceId { get; init; } = "";
}

record ScheduleDto
{
    public int Id { get; init; }
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string StartTime { get; init; } = "";
    public string? EndTime { get; init; }
    public bool IsAllDay { get; init; }
    public string Color { get; init; } = "#6c5ce7";
    public string CreatedAt { get; init; } = "";
    public string DeviceId { get; init; } = "";
}
