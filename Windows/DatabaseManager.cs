using System.Data.SQLite;
using System.Globalization;
using System.IO;

namespace TimeTracker
{
    public class DatabaseManager
    {
        private readonly string _dbPath;
        private readonly object _initLock = new();
        private bool _initialized;

        private const string DateFormat = "yyyy-MM-dd HH:mm:ss";
        public const int MaxSyncLimit = 10000;
        public const int DefaultRetentionDays = 90;

        public DatabaseManager(string dbPath = "time_tracker.db")
        {
            var dir = AppSettings.IsPortable ? AppDomain.CurrentDomain.BaseDirectory
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeTracker");
            if (!AppSettings.IsPortable) Directory.CreateDirectory(dir);
            _dbPath = Path.Combine(dir, dbPath);
            // 如果设置了 DatabasePassword，使用加密连接
            if (!string.IsNullOrEmpty(AppSettings.DatabasePassword))
                _dbPath += $";Password={AppSettings.DatabasePassword}";
            EnsureInitialized();
        }

        private void EnsureInitialized()
        {
            lock (_initLock)
            {
                if (_initialized) return;
                _initialized = true;

                using var connection = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                connection.Open();

                // 启用 WAL 模式
                using var walCmd = new SQLiteCommand("PRAGMA journal_mode=WAL;", connection);
                walCmd.ExecuteNonQuery();

                using var syncCmd = new SQLiteCommand("PRAGMA synchronous=NORMAL;", connection);
                syncCmd.ExecuteNonQuery();

                string[] tables = [
                    "CREATE TABLE IF NOT EXISTS categories (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, color TEXT DEFAULT '#3498db', description TEXT DEFAULT '')",
                    "CREATE TABLE IF NOT EXISTS time_records (id INTEGER PRIMARY KEY AUTOINCREMENT, process_name TEXT NOT NULL, window_title TEXT, usage_time INTEGER NOT NULL, date TEXT NOT NULL, device_id TEXT NOT NULL, category_id INTEGER DEFAULT NULL, is_foreground INTEGER DEFAULT 1, activity_id INTEGER DEFAULT NULL, user_id INTEGER DEFAULT NULL)",
                    "CREATE TABLE IF NOT EXISTS devices (device_id TEXT PRIMARY KEY, device_name TEXT NOT NULL, platform TEXT NOT NULL, last_sync TEXT)",
                    "CREATE TABLE IF NOT EXISTS users (id INTEGER PRIMARY KEY AUTOINCREMENT, username TEXT UNIQUE NOT NULL, password TEXT NOT NULL, token TEXT, expires_at TEXT, created_at TEXT)",
                    "CREATE TABLE IF NOT EXISTS activities (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, color TEXT DEFAULT '#6c5ce7', icon TEXT DEFAULT '\U0001f4cc')",
                    "CREATE TABLE IF NOT EXISTS todo_items (id INTEGER PRIMARY KEY AUTOINCREMENT, title TEXT NOT NULL, description TEXT DEFAULT '', is_completed INTEGER DEFAULT 0, priority INTEGER DEFAULT 1, due_date TEXT, created_at TEXT NOT NULL, completed_at TEXT, device_id TEXT NOT NULL, user_id INTEGER DEFAULT NULL)",
                    "CREATE TABLE IF NOT EXISTS schedules (id INTEGER PRIMARY KEY AUTOINCREMENT, title TEXT NOT NULL, description TEXT DEFAULT '', start_time TEXT NOT NULL, end_time TEXT, is_all_day INTEGER DEFAULT 0, color TEXT DEFAULT '#6c5ce7', created_at TEXT NOT NULL, device_id TEXT NOT NULL, user_id INTEGER DEFAULT NULL)",
                    "CREATE TABLE IF NOT EXISTS manual_records (id INTEGER PRIMARY KEY AUTOINCREMENT, title TEXT NOT NULL, description TEXT DEFAULT '', start_time TEXT NOT NULL, duration_minutes INTEGER DEFAULT 0, category_id INTEGER DEFAULT NULL, activity_id INTEGER DEFAULT NULL, user_id INTEGER DEFAULT NULL, created_at TEXT NOT NULL)",
                    "CREATE TABLE IF NOT EXISTS goals (id INTEGER PRIMARY KEY AUTOINCREMENT, title TEXT NOT NULL, description TEXT DEFAULT '', status TEXT DEFAULT 'active', total_minutes_goal INTEGER DEFAULT 0, created_at TEXT NOT NULL, completed_at TEXT, user_id INTEGER DEFAULT NULL)",
                    "CREATE TABLE IF NOT EXISTS goal_phases (id INTEGER PRIMARY KEY AUTOINCREMENT, goal_id INTEGER NOT NULL, title TEXT NOT NULL, description TEXT DEFAULT '', phase_order INTEGER NOT NULL, estimated_minutes INTEGER DEFAULT 0, actual_minutes INTEGER DEFAULT 0, status TEXT DEFAULT 'pending', effective_ratio REAL DEFAULT 0, user_notes TEXT DEFAULT '', created_at TEXT NOT NULL, completed_at TEXT, FOREIGN KEY(goal_id) REFERENCES goals(id))"
                ];

                // CA2100: 安全 — tables 数组中的 SQL 均为硬编码 DDL 常量，无可注入的用户输入
#pragma warning disable CA2100
                foreach (var sql in tables)
                {
                    using var cmd = new SQLiteCommand(sql, connection);
                    cmd.ExecuteNonQuery();
                }
#pragma warning restore CA2100

                try {
                    using var alterCmd = new SQLiteCommand("ALTER TABLE time_records ADD COLUMN activity_id INTEGER DEFAULT NULL", connection);
                    alterCmd.ExecuteNonQuery();
                } catch { }

                try {
                    using var alterCmd = new SQLiteCommand("ALTER TABLE time_records ADD COLUMN user_id INTEGER DEFAULT NULL", connection);
                    alterCmd.ExecuteNonQuery();
                } catch { }
            }
        }

        private SQLiteConnection CreateConnection()
        {
            var connection = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
            connection.Open();
            return connection;
        }

        public void InsertTimeRecord(string processName, string windowTitle, long usageTime,
            string deviceId, int? categoryId = null, bool isForeground = true, int? activityId = null, int? userId = null)
        {
            const string insertQuery = @"
                INSERT INTO time_records (process_name, window_title, usage_time, date, device_id, category_id, is_foreground, activity_id, user_id)
                VALUES (@processName, @windowTitle, @usageTime, @date, @deviceId, @categoryId, @isForeground, @activityId, @userId)
            ";

            using var connection = CreateConnection();
            using var command = new SQLiteCommand(insertQuery, connection);
            command.Parameters.AddWithValue("@processName", processName);
            command.Parameters.AddWithValue("@windowTitle", (object?)windowTitle ?? DBNull.Value);
            command.Parameters.AddWithValue("@usageTime", usageTime);
            command.Parameters.AddWithValue("@date", DateTime.Now.ToString(DateFormat, CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("@deviceId", deviceId);
            command.Parameters.AddWithValue("@categoryId", categoryId.HasValue ? categoryId.Value : DBNull.Value);
            command.Parameters.AddWithValue("@isForeground", isForeground ? 1 : 0);
            command.Parameters.AddWithValue("@activityId", activityId.HasValue ? activityId.Value : DBNull.Value);
            command.Parameters.AddWithValue("@userId", userId.HasValue ? userId.Value : DBNull.Value);
            command.ExecuteNonQuery();
        }

        public void InsertTimeRecords(IEnumerable<TimeRecordData> records)
        {
            // 修复：空列表检查，避免无效连接/事务开销
            var list = records as List<TimeRecordData> ?? records.ToList();
            if (list.Count == 0) return;

            using var connection = CreateConnection();
            using var transaction = connection.BeginTransaction();
            try
            {
                const string insertQuery = @"
                    INSERT INTO time_records (process_name, window_title, usage_time, date, device_id, category_id, is_foreground, activity_id, user_id)
                    VALUES (@processName, @windowTitle, @usageTime, @date, @deviceId, @categoryId, @isForeground, @activityId, @userId)
                ";

                using var command = new SQLiteCommand(insertQuery, connection, transaction);
                foreach (var r in list)
                {
                    command.Parameters.Clear();
                    command.Parameters.AddWithValue("@processName", r.ProcessName);
                    command.Parameters.AddWithValue("@windowTitle", (object?)r.WindowTitle ?? DBNull.Value);
                    command.Parameters.AddWithValue("@usageTime", r.UsageTime);
                    command.Parameters.AddWithValue("@date", r.Date);
                    command.Parameters.AddWithValue("@deviceId", r.DeviceId);
                    command.Parameters.AddWithValue("@categoryId", r.CategoryId.HasValue ? r.CategoryId.Value : DBNull.Value);
                    command.Parameters.AddWithValue("@isForeground", r.IsForeground ? 1 : 0);
                    command.Parameters.AddWithValue("@activityId", r.ActivityId.HasValue ? r.ActivityId.Value : DBNull.Value);
                    command.Parameters.AddWithValue("@userId", r.UserId.HasValue ? r.UserId.Value : DBNull.Value);
                    command.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public void AddCategory(string name, string color = "#3498db", string description = "")
        {
            const string insertQuery = @"
                INSERT INTO categories (name, color, description)
                VALUES (@name, @color, @description)
            ";

            using var connection = CreateConnection();
            using var command = new SQLiteCommand(insertQuery, connection);
            command.Parameters.AddWithValue("@name", name);
            command.Parameters.AddWithValue("@color", color);
            command.Parameters.AddWithValue("@description", description);
            command.ExecuteNonQuery();
        }

        public List<CategoryData> GetCategories()
        {
            var categories = new List<CategoryData>();
            const string query = "SELECT * FROM categories ORDER BY name";

            using var connection = CreateConnection();
            using var command = new SQLiteCommand(query, connection);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                categories.Add(new CategoryData
                {
                    Id = Convert.ToInt32(reader["id"], CultureInfo.InvariantCulture),
                    Name = reader["name"].ToString()!,
                    Color = reader["color"].ToString()!,
                    Description = reader["description"].ToString()!
                });
            }
            return categories;
        }

        public void UpdateCategory(int id, string name, string color, string description)
        {
            const string updateQuery = @"
                UPDATE categories
                SET name = @name, color = @color, description = @description
                WHERE id = @id
            ";

            using var connection = CreateConnection();
            using var command = new SQLiteCommand(updateQuery, connection);
            command.Parameters.AddWithValue("@id", id);
            command.Parameters.AddWithValue("@name", name);
            command.Parameters.AddWithValue("@color", color);
            command.Parameters.AddWithValue("@description", description);
            command.ExecuteNonQuery();
        }

        public void DeleteCategory(int id)
        {
            const string deleteQuery = "DELETE FROM categories WHERE id = @id";
            using var connection = CreateConnection();
            using var command = new SQLiteCommand(deleteQuery, connection);
            command.Parameters.AddWithValue("@id", id);
            command.ExecuteNonQuery();
        }

        // ===== Activities CRUD =====
        public List<ActivityData> GetActivities()
        {
            var list = new List<ActivityData>();
            const string query = "SELECT * FROM activities ORDER BY id";
            using var connection = CreateConnection();
            using var command = new SQLiteCommand(query, connection);
            using var reader = command.ExecuteReader();
            while (reader.Read())
                list.Add(new ActivityData
                {
                    Id = Convert.ToInt32(reader["id"], CultureInfo.InvariantCulture),
                    Name = reader["name"].ToString()!,
                    Color = reader["color"].ToString()!,
                    Icon = reader["icon"].ToString()!
                });
            return list;
        }

        public void AddActivity(string name, string color = "#6c5ce7", string icon = "\U0001f4cc")
        {
            const string q = "INSERT INTO activities (name, color, icon) VALUES (@n, @c, @i)";
            using var c = CreateConnection();
            using var cmd = new SQLiteCommand(q, c);
            cmd.Parameters.AddWithValue("@n", name); cmd.Parameters.AddWithValue("@c", color);
            cmd.Parameters.AddWithValue("@i", icon); cmd.ExecuteNonQuery();
        }

        public void DeleteActivity(int id)
        {
            using var c = CreateConnection();
            using var cmd = new SQLiteCommand("DELETE FROM activities WHERE id=@id", c);
            cmd.Parameters.AddWithValue("@id", id); cmd.ExecuteNonQuery();
        }

        /// <summary>获取指定活动下的进程使用统计</summary>
        public List<ProcessUsageData> GetTopProcessesByActivity(DateTime start, DateTime end, int activityId)
        {
            var result = new List<ProcessUsageData>();
            const string query = @"SELECT tr.process_name, SUM(tr.usage_time) as total_usage, c.name as category_name
                FROM time_records tr LEFT JOIN categories c ON tr.category_id=c.id
                WHERE tr.date BETWEEN @s AND @e AND tr.activity_id=@aid
                GROUP BY tr.process_name ORDER BY total_usage DESC";
            using var conn = CreateConnection();
            using var cmd = new SQLiteCommand(query, conn);
            cmd.Parameters.AddWithValue("@s", start.ToString(DateFormat, CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@e", end.ToString(DateFormat, CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@aid", activityId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                result.Add(new ProcessUsageData
                {
                    ProcessName = r["process_name"].ToString()!,
                    TotalUsage = Convert.ToInt64(r["total_usage"], CultureInfo.InvariantCulture),
                    CategoryName = r["category_name"] != DBNull.Value ? r["category_name"].ToString()! : "未分类"
                });
            return result;
        }

        /// <summary>获取所有活动的使用时长汇总</summary>
        public List<ActivityUsageData> GetStatsByActivity(DateTime start, DateTime end)
        {
            var result = new List<ActivityUsageData>();
            const string query = @"SELECT a.id, a.name, a.color, COALESCE(SUM(tr.usage_time),0) as total_usage
                FROM activities a LEFT JOIN time_records tr ON a.id=tr.activity_id AND tr.date BETWEEN @s AND @e
                GROUP BY a.id ORDER BY total_usage DESC";
            using var conn = CreateConnection();
            using var cmd = new SQLiteCommand(query, conn);
            cmd.Parameters.AddWithValue("@s", start.ToString(DateFormat, CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@e", end.ToString(DateFormat, CultureInfo.InvariantCulture));
            using var r = cmd.ExecuteReader();
            while (r.Read())
                result.Add(new ActivityUsageData
                {
                    Id = Convert.ToInt32(r["id"], CultureInfo.InvariantCulture),
                    Name = r["name"].ToString()!,
                    Color = r["color"].ToString()!,
                    TotalUsage = Convert.ToInt64(r["total_usage"], CultureInfo.InvariantCulture)
                });
            return result;
        }

        public List<TimeRecordData> GetTimeRecords(DateTime startDate, DateTime endDate, int? userId = null)
        {
            var records = new List<TimeRecordData>();
            var query = @"
                SELECT tr.*, c.name as category_name, c.color as category_color
                FROM time_records tr
                LEFT JOIN categories c ON tr.category_id = c.id
                WHERE tr.date BETWEEN @startDate AND @endDate
            ";
            if (userId.HasValue) query += " AND tr.user_id = @userId ";
            query += "ORDER BY tr.date DESC";

            using var connection = CreateConnection();
            using var command = new SQLiteCommand(query, connection);
            command.Parameters.AddWithValue("@startDate", startDate.ToString(DateFormat, CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("@endDate", endDate.ToString(DateFormat, CultureInfo.InvariantCulture));
            if (userId.HasValue) command.Parameters.AddWithValue("@userId", userId.Value);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                records.Add(MapTimeRecord(reader));
            }
            return records;
        }

        public List<ProcessUsageData> GetTopProcesses(DateTime startDate, DateTime endDate)
        {
            var result = new List<ProcessUsageData>();
            const string query = @"
                SELECT tr.process_name, SUM(tr.usage_time) as total_usage, c.name as category_name
                FROM time_records tr
                LEFT JOIN categories c ON tr.category_id = c.id
                WHERE tr.date BETWEEN @startDate AND @endDate
                GROUP BY tr.process_name
                ORDER BY total_usage DESC
            ";

            using var connection = CreateConnection();
            using var command = new SQLiteCommand(query, connection);
            command.Parameters.AddWithValue("@startDate", startDate.ToString(DateFormat, CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("@endDate", endDate.ToString(DateFormat, CultureInfo.InvariantCulture));
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new ProcessUsageData
                {
                    ProcessName = reader["process_name"].ToString()!,
                    TotalUsage = Convert.ToInt64(reader["total_usage"], CultureInfo.InvariantCulture),
                    CategoryName = reader["category_name"] != DBNull.Value
                        ? reader["category_name"].ToString()!
                        : "未分类"
                });
            }
            return result;
        }

        public List<CategoryStatsData> GetStatsByCategory(DateTime startDate, DateTime endDate)
        {
            var result = new List<CategoryStatsData>();
            const string query = @"
                SELECT c.name as category_name, c.color as category_color, SUM(tr.usage_time) as total_usage
                FROM time_records tr
                LEFT JOIN categories c ON tr.category_id = c.id
                WHERE tr.date BETWEEN @startDate AND @endDate
                GROUP BY tr.category_id
                ORDER BY total_usage DESC
            ";

            using var connection = CreateConnection();
            using var command = new SQLiteCommand(query, connection);
            command.Parameters.AddWithValue("@startDate", startDate.ToString(DateFormat, CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("@endDate", endDate.ToString(DateFormat, CultureInfo.InvariantCulture));
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new CategoryStatsData
                {
                    CategoryName = reader["category_name"] != DBNull.Value
                        ? reader["category_name"].ToString()!
                        : "未分类",
                    TotalUsage = Convert.ToInt64(reader["total_usage"], CultureInfo.InvariantCulture)
                });
            }
            return result;
        }

        public List<ForegroundStatsData> GetStatsByForeground(DateTime startDate, DateTime endDate)
        {
            var result = new List<ForegroundStatsData>();
            const string query = @"
                SELECT CASE WHEN is_foreground = 1 THEN '前台' ELSE '后台' END as type, SUM(usage_time) as total_usage
                FROM time_records
                WHERE date BETWEEN @startDate AND @endDate
                GROUP BY is_foreground
            ";

            using var connection = CreateConnection();
            using var command = new SQLiteCommand(query, connection);
            command.Parameters.AddWithValue("@startDate", startDate.ToString(DateFormat, CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("@endDate", endDate.ToString(DateFormat, CultureInfo.InvariantCulture));
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new ForegroundStatsData
                {
                    Type = reader["type"].ToString()!,
                    TotalUsage = Convert.ToInt64(reader["total_usage"], CultureInfo.InvariantCulture)
                });
            }
            return result;
        }

        /// <summary>
        /// 将指定进程名下的所有 records 更新为新的 categoryId（忽略大小写）
        /// </summary>
        public int UpdateProcessCategory(string processName, int categoryId)
        {
            const string query = "UPDATE time_records SET category_id = @categoryId WHERE process_name = @processName COLLATE NOCASE";
            using var connection = CreateConnection();
            using var command = new SQLiteCommand(query, connection);
            command.Parameters.AddWithValue("@categoryId", categoryId);
            command.Parameters.AddWithValue("@processName", processName);
            return command.ExecuteNonQuery();
        }

        public bool HasTimeRecordsForPeriod(DateTime startDate, DateTime endDate, string processName, string deviceId)
        {
            const string query = @"
                SELECT COUNT(*) FROM time_records
                WHERE process_name = @processName AND device_id = @deviceId
                AND date BETWEEN @startDate AND @endDate
            ";

            using var connection = CreateConnection();
            using var command = new SQLiteCommand(query, connection);
            command.Parameters.AddWithValue("@processName", processName);
            command.Parameters.AddWithValue("@deviceId", deviceId);
            command.Parameters.AddWithValue("@startDate", startDate.ToString(DateFormat, CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("@endDate", endDate.ToString(DateFormat, CultureInfo.InvariantCulture));
            var result = command.ExecuteScalar();
            return Convert.ToInt32(result, CultureInfo.InvariantCulture) > 0;
        }

        public void DeleteOldRecords(DateTime cutoffDate)
        {
            const string query = "DELETE FROM time_records WHERE date < @cutoffDate";
            using var connection = CreateConnection();
            using var command = new SQLiteCommand(query, connection);
            command.Parameters.AddWithValue("@cutoffDate", cutoffDate.ToString(DateFormat, CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }

        public string BackupDatabase(string? backupDir = null)
        {
            var src = _dbPath;
            // 移除附加的连接字符串参数
            if (src.Contains(";", StringComparison.Ordinal)) src = src.Split(';')[0];
            if (!File.Exists(src)) return "";

            var dir = backupDir ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TimeTracker", "Backups");
            Directory.CreateDirectory(dir);

            var dst = Path.Combine(dir, $"time_tracker_backup_{DateTime.Now:yyyyMMdd_HHmmss}.db");
            File.Copy(src, dst, true);
            return dst;
        }

        /// <summary>按小时汇总（今日时间线）</summary>
        public List<(int Hour, string ProcessName, long UsageMs)> GetHourlyBreakdown(DateTime date)
        {
            var result = new List<(int, string, long)>();
            var start = date.Date.ToString(DateFormat, CultureInfo.InvariantCulture);
            var end = date.Date.AddDays(1).ToString(DateFormat, CultureInfo.InvariantCulture);
            const string q = @"SELECT CAST(substr(date,12,2) AS INTEGER) as hour, process_name, SUM(usage_time) as total
                FROM time_records WHERE date BETWEEN @s AND @e
                GROUP BY hour, process_name ORDER BY hour, total DESC";
            using var c = CreateConnection();
            using var cmd = new SQLiteCommand(q, c);
            cmd.Parameters.AddWithValue("@s", start);
            cmd.Parameters.AddWithValue("@e", end);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                result.Add((Convert.ToInt32(r["hour"], CultureInfo.InvariantCulture), r["process_name"].ToString()!, Convert.ToInt64(r["total"], CultureInfo.InvariantCulture)));
            return result;
        }

        /// <summary>
        /// 批量获取现有记录的去重键，用于导入去重。统一使用 yyyyMMdd|进程名|设备ID 格式
        /// </summary>
        public HashSet<string> GetAllRecordKeys(int? userId = null)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var query = "SELECT date, process_name, device_id FROM time_records";
            if (userId.HasValue) query += " WHERE user_id = @userId";
            using var connection = CreateConnection();
            using var command = new SQLiteCommand(query, connection);
            if (userId.HasValue) command.Parameters.AddWithValue("@userId", userId.Value);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var dateStr = reader["date"]?.ToString() ?? "";
                var proc = reader["process_name"]?.ToString() ?? "";
                var dev = reader["device_id"]?.ToString() ?? "";
                if (DateTime.TryParse(dateStr, out var date))
                    keys.Add($"{date:yyyyMMdd}|{proc}|{dev}");
            }
            return keys;
        }

        public void RegisterDevice(string deviceId, string deviceName, string platform)
        {
            const string upsertQuery = @"
                INSERT OR REPLACE INTO devices (device_id, device_name, platform, last_sync)
                VALUES (@deviceId, @deviceName, @platform, @lastSync)
            ";
            using var connection = CreateConnection();
            using var command = new SQLiteCommand(upsertQuery, connection);
            command.Parameters.AddWithValue("@deviceId", deviceId);
            command.Parameters.AddWithValue("@deviceName", deviceName);
            command.Parameters.AddWithValue("@platform", platform);
            command.Parameters.AddWithValue("@lastSync", DateTime.Now.ToString(DateFormat, CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }

        private static TimeRecordData MapTimeRecord(SQLiteDataReader reader)
        {
            return new TimeRecordData
            {
                Id = Convert.ToInt32(reader["id"], CultureInfo.InvariantCulture),
                ProcessName = reader["process_name"].ToString()!,
                WindowTitle = reader["window_title"] != DBNull.Value
                    ? reader["window_title"].ToString()!
                    : null,
                UsageTime = Convert.ToInt64(reader["usage_time"], CultureInfo.InvariantCulture),
                Date = reader["date"].ToString()!,
                DeviceId = reader["device_id"].ToString()!,
                CategoryId = reader["category_id"] != DBNull.Value
                    ? Convert.ToInt32(reader["category_id"], CultureInfo.InvariantCulture)
                    : null,
                IsForeground = Convert.ToInt32(reader["is_foreground"], CultureInfo.InvariantCulture) == 1,
                CategoryName = reader["category_name"] != DBNull.Value
                    ? reader["category_name"].ToString()!
                    : null,
                UserId = reader["user_id"] != DBNull.Value
                    ? Convert.ToInt32(reader["user_id"], CultureInfo.InvariantCulture)
                    : null
            };
        }

        // ===== Todo CRUD =====

        public void InsertTodo(string title, string description, int priority, string? dueDate, string deviceId, int? userId = null)
        {
            const string sql = "INSERT INTO todo_items(title,description,priority,due_date,created_at,device_id,user_id) VALUES(@t,@d,@p,@dd,@ca,@di,@ui)";
            using var conn = CreateConnection();
            using var cmd = new SQLiteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@t", title);
            cmd.Parameters.AddWithValue("@d", description);
            cmd.Parameters.AddWithValue("@p", priority);
            cmd.Parameters.AddWithValue("@dd", (object?)dueDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ca", DateTime.Now.ToString(DateFormat, CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@di", deviceId);
            cmd.Parameters.AddWithValue("@ui", userId.HasValue ? userId.Value : DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        public void UpdateTodo(TodoItemData item)
        {
            const string sql = "UPDATE todo_items SET title=@t,description=@d,is_completed=@ic,priority=@p,due_date=@dd,completed_at=@ca WHERE id=@id";
            using var conn = CreateConnection();
            using var cmd = new SQLiteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@t", item.Title);
            cmd.Parameters.AddWithValue("@d", item.Description);
            cmd.Parameters.AddWithValue("@ic", item.IsCompleted ? 1 : 0);
            cmd.Parameters.AddWithValue("@p", item.Priority);
            cmd.Parameters.AddWithValue("@dd", (object?)item.DueDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ca", item.CompletedAt != null ? item.CompletedAt : DBNull.Value);
            cmd.Parameters.AddWithValue("@id", item.Id);
            cmd.ExecuteNonQuery();
        }

        public void ToggleTodo(int id, bool completed)
        {
            var completedAt = completed ? DateTime.Now.ToString(DateFormat, CultureInfo.InvariantCulture) : null;
            const string sql = "UPDATE todo_items SET is_completed=@ic,completed_at=@ca WHERE id=@id";
            using var conn = CreateConnection();
            using var cmd = new SQLiteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@ic", completed ? 1 : 0);
            cmd.Parameters.AddWithValue("@ca", (object?)completedAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public void DeleteTodo(int id)
        {
            const string sql = "DELETE FROM todo_items WHERE id=@id";
            using var conn = CreateConnection();
            using var cmd = new SQLiteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public List<TodoItemData> GetTodos(bool includeCompleted = true, int? userId = null)
        {
            var result = new List<TodoItemData>();
            var sql = "SELECT * FROM todo_items";
            if (!includeCompleted) sql += " WHERE is_completed=0";
            else if (userId.HasValue) sql += " WHERE user_id=@ui";
            if (!includeCompleted && userId.HasValue) sql += " AND user_id=@ui";
            sql += " ORDER BY is_completed ASC, priority DESC, due_date ASC, created_at DESC";
            using var conn = CreateConnection();
            using var cmd = new SQLiteCommand(sql, conn);
            if (userId.HasValue) cmd.Parameters.AddWithValue("@ui", userId.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read()) result.Add(MapTodo(r));
            return result;
        }

        private static TodoItemData MapTodo(SQLiteDataReader r)
        {
            return new TodoItemData
            {
                Id = Convert.ToInt32(r["id"], CultureInfo.InvariantCulture),
                Title = r["title"].ToString()!,
                Description = r["description"]?.ToString() ?? "",
                IsCompleted = Convert.ToInt32(r["is_completed"], CultureInfo.InvariantCulture) == 1,
                Priority = Convert.ToInt32(r["priority"], CultureInfo.InvariantCulture),
                DueDate = r["due_date"] != DBNull.Value ? r["due_date"].ToString() : null,
                CreatedAt = r["created_at"].ToString()!,
                CompletedAt = r["completed_at"] != DBNull.Value ? r["completed_at"].ToString() : null,
                DeviceId = r["device_id"].ToString()!,
                UserId = r["user_id"] != DBNull.Value ? Convert.ToInt32(r["user_id"], CultureInfo.InvariantCulture) : null
            };
        }

        // ===== Schedule CRUD =====

        public void InsertSchedule(string title, string description, string startTime, string? endTime, bool isAllDay, string color, string deviceId, int? userId = null)
        {
            const string sql = "INSERT INTO schedules(title,description,start_time,end_time,is_all_day,color,created_at,device_id,user_id) VALUES(@t,@d,@st,@et,@ia,@c,@ca,@di,@ui)";
            using var conn = CreateConnection();
            using var cmd = new SQLiteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@t", title);
            cmd.Parameters.AddWithValue("@d", description);
            cmd.Parameters.AddWithValue("@st", startTime);
            cmd.Parameters.AddWithValue("@et", (object?)endTime ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ia", isAllDay ? 1 : 0);
            cmd.Parameters.AddWithValue("@c", color);
            cmd.Parameters.AddWithValue("@ca", DateTime.Now.ToString(DateFormat, CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@di", deviceId);
            cmd.Parameters.AddWithValue("@ui", userId.HasValue ? userId.Value : DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        public void UpdateSchedule(ScheduleData item)
        {
            const string sql = "UPDATE schedules SET title=@t,description=@d,start_time=@st,end_time=@et,is_all_day=@ia,color=@c WHERE id=@id";
            using var conn = CreateConnection();
            using var cmd = new SQLiteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@t", item.Title);
            cmd.Parameters.AddWithValue("@d", item.Description);
            cmd.Parameters.AddWithValue("@st", item.StartTime);
            cmd.Parameters.AddWithValue("@et", (object?)item.EndTime ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ia", item.IsAllDay ? 1 : 0);
            cmd.Parameters.AddWithValue("@c", item.Color);
            cmd.Parameters.AddWithValue("@id", item.Id);
            cmd.ExecuteNonQuery();
        }

        public void DeleteSchedule(int id)
        {
            const string sql = "DELETE FROM schedules WHERE id=@id";
            using var conn = CreateConnection();
            using var cmd = new SQLiteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public List<ScheduleData> GetSchedules(DateTime? from = null, DateTime? to = null, int? userId = null)
        {
            var result = new List<ScheduleData>();
            var sql = "SELECT * FROM schedules";
            var conditions = new List<string>();
            if (from.HasValue) conditions.Add("start_time >= @from");
            if (to.HasValue) conditions.Add("start_time <= @to");
            if (userId.HasValue) conditions.Add("user_id=@ui");
            if (conditions.Count > 0) sql += " WHERE " + string.Join(" AND ", conditions);
            sql += " ORDER BY start_time ASC";
            using var conn = CreateConnection();
            // CA2100: 安全 — sql 中的表名/列名均为硬编码常量，参数使用 @from/@to/@ui 参数化
#pragma warning disable CA2100
            using var cmd = new SQLiteCommand(sql, conn);
#pragma warning restore CA2100
            if (from.HasValue) cmd.Parameters.AddWithValue("@from", from.Value.ToString(DateFormat, CultureInfo.InvariantCulture));
            if (to.HasValue) cmd.Parameters.AddWithValue("@to", to.Value.ToString(DateFormat, CultureInfo.InvariantCulture));
            if (userId.HasValue) cmd.Parameters.AddWithValue("@ui", userId.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read()) result.Add(MapSchedule(r));
            return result;
        }

        private static ScheduleData MapSchedule(SQLiteDataReader r)
        {
            return new ScheduleData
            {
                Id = Convert.ToInt32(r["id"], CultureInfo.InvariantCulture),
                Title = r["title"].ToString()!,
                Description = r["description"]?.ToString() ?? "",
                StartTime = r["start_time"].ToString()!,
                EndTime = r["end_time"] != DBNull.Value ? r["end_time"].ToString() : null,
                IsAllDay = Convert.ToInt32(r["is_all_day"], CultureInfo.InvariantCulture) == 1,
                Color = r["color"].ToString()!,
                CreatedAt = r["created_at"].ToString()!,
                DeviceId = r["device_id"].ToString()!,
                UserId = r["user_id"] != DBNull.Value ? Convert.ToInt32(r["user_id"], CultureInfo.InvariantCulture) : null
            };
        }

        // ===== Manual Records CRUD =====

        public void InsertManualRecord(string title, string description, string startTime, int durationMinutes,
            int? categoryId, int? activityId, int? userId)
        {
            const string sql = "INSERT INTO manual_records(title,description,start_time,duration_minutes,category_id,activity_id,user_id,created_at) VALUES(@t,@d,@st,@dm,@ci,@ai,@ui,@ca)";
            using var conn = CreateConnection();
            using var cmd = new SQLiteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@t", title);
            cmd.Parameters.AddWithValue("@d", description);
            cmd.Parameters.AddWithValue("@st", startTime);
            cmd.Parameters.AddWithValue("@dm", durationMinutes);
            cmd.Parameters.AddWithValue("@ci", categoryId.HasValue ? categoryId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@ai", activityId.HasValue ? activityId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@ui", userId.HasValue ? userId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@ca", DateTime.Now.ToString(DateFormat, CultureInfo.InvariantCulture));
            cmd.ExecuteNonQuery();
        }

        public void DeleteManualRecord(int id)
        {
            const string sql = "DELETE FROM manual_records WHERE id=@id";
            using var conn = CreateConnection();
            using var cmd = new SQLiteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public List<ManualRecordData> GetManualRecords(DateTime? from = null, DateTime? to = null, int? userId = null)
        {
            var result = new List<ManualRecordData>();
            var sql = "SELECT mr.* FROM manual_records mr";
            var conditions = new List<string>();
            if (from.HasValue) conditions.Add("mr.start_time >= @from");
            if (to.HasValue) conditions.Add("mr.start_time <= @to");
            if (userId.HasValue) conditions.Add("mr.user_id=@ui");
            if (conditions.Count > 0) sql += " WHERE " + string.Join(" AND ", conditions);
            sql += " ORDER BY mr.start_time DESC";
            using var conn = CreateConnection();
            // CA2100: 安全 — sql 中的表名/列名均为硬编码常量，参数使用 @from/@to/@ui 参数化
#pragma warning disable CA2100
            using var cmd = new SQLiteCommand(sql, conn);
#pragma warning restore CA2100
            if (from.HasValue) cmd.Parameters.AddWithValue("@from", from.Value.ToString(DateFormat, CultureInfo.InvariantCulture));
            if (to.HasValue) cmd.Parameters.AddWithValue("@to", to.Value.ToString(DateFormat, CultureInfo.InvariantCulture));
            if (userId.HasValue) cmd.Parameters.AddWithValue("@ui", userId.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read()) result.Add(MapManualRecord(r));
            return result;
        }

        private static ManualRecordData MapManualRecord(SQLiteDataReader r)
        {
            return new ManualRecordData
            {
                Id = Convert.ToInt32(r["id"], CultureInfo.InvariantCulture),
                Title = r["title"].ToString()!,
                Description = r["description"]?.ToString() ?? "",
                StartTime = r["start_time"].ToString()!,
                DurationMinutes = Convert.ToInt32(r["duration_minutes"], CultureInfo.InvariantCulture),
                CategoryId = r["category_id"] != DBNull.Value ? Convert.ToInt32(r["category_id"], CultureInfo.InvariantCulture) : null,
                ActivityId = r["activity_id"] != DBNull.Value ? Convert.ToInt32(r["activity_id"], CultureInfo.InvariantCulture) : null,
                UserId = r["user_id"] != DBNull.Value ? Convert.ToInt32(r["user_id"], CultureInfo.InvariantCulture) : null,
                CreatedAt = r["created_at"].ToString()!
            };
        }

        // ======================== 目标管理 ========================

        public int InsertGoal(string title, string description = "", int totalMinutesGoal = 0)
        {
            const string q = @"INSERT INTO goals (title, description, total_minutes_goal, created_at)
                VALUES (@t, @d, @m, @c); SELECT last_insert_rowid();";
            using var c = CreateConnection();
            using var cmd = new SQLiteCommand(q, c);
            cmd.Parameters.AddWithValue("@t", title);
            cmd.Parameters.AddWithValue("@d", description);
            cmd.Parameters.AddWithValue("@m", totalMinutesGoal);
            cmd.Parameters.AddWithValue("@c", DateTime.Now.ToString(DateFormat, CultureInfo.InvariantCulture));
            return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        public void InsertGoalPhase(int goalId, string title, string desc, int order, int estMinutes)
        {
            const string q = @"INSERT INTO goal_phases (goal_id, title, description, phase_order, estimated_minutes, created_at)
                VALUES (@g, @t, @d, @o, @e, @c)";
            using var c = CreateConnection();
            using var cmd = new SQLiteCommand(q, c);
            cmd.Parameters.AddWithValue("@g", goalId);
            cmd.Parameters.AddWithValue("@t", title); cmd.Parameters.AddWithValue("@d", desc);
            cmd.Parameters.AddWithValue("@o", order); cmd.Parameters.AddWithValue("@e", estMinutes);
            cmd.Parameters.AddWithValue("@c", DateTime.Now.ToString(DateFormat, CultureInfo.InvariantCulture));
            cmd.ExecuteNonQuery();
        }

        public List<GoalData> GetGoals()
        {
            var list = new List<GoalData>();
            using var c = CreateConnection();
            using var cmd = new SQLiteCommand("SELECT * FROM goals ORDER BY created_at DESC", c);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new GoalData { Id = Convert.ToInt32(r["id"]), Title = r["title"].ToString()!,
                    Description = r["description"]?.ToString() ?? "", Status = r["status"]?.ToString() ?? "active",
                    TotalMinutesGoal = Convert.ToInt32(r["total_minutes_goal"]), CreatedAt = r["created_at"].ToString()! });
            return list;
        }

        public List<GoalPhaseData> GetGoalPhases(int goalId)
        {
            var list = new List<GoalPhaseData>();
            using var c = CreateConnection();
            using var cmd = new SQLiteCommand("SELECT * FROM goal_phases WHERE goal_id=@g ORDER BY phase_order", c);
            cmd.Parameters.AddWithValue("@g", goalId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new GoalPhaseData { Id = Convert.ToInt32(r["id"]), GoalId = goalId,
                    Title = r["title"].ToString()!, Description = r["description"]?.ToString() ?? "",
                    PhaseOrder = Convert.ToInt32(r["phase_order"]), EstimatedMinutes = Convert.ToInt32(r["estimated_minutes"]),
                    ActualMinutes = Convert.ToInt32(r["actual_minutes"]), Status = r["status"]?.ToString() ?? "pending",
                    EffectiveRatio = Convert.ToDouble(r["effective_ratio"], CultureInfo.InvariantCulture) });
            return list;
        }

        public void UpdateGoalStatus(int goalId, string status) { Exec("UPDATE goals SET status=@s, completed_at=@c WHERE id=@id",
            ("@s", status), ("@c", status == "completed" ? DateTime.Now.ToString(DateFormat, CultureInfo.InvariantCulture) : (object?)null), ("@id", goalId)); }

        public void CompletePhase(int phaseId, int actualMinutes, double effectiveRatio, string notes)
        {
            Exec(@"UPDATE goal_phases SET status='completed', actual_minutes=@m, effective_ratio=@r,
                user_notes=@n, completed_at=@c WHERE id=@id",
                ("@m", actualMinutes), ("@r", effectiveRatio), ("@n", notes),
                ("@c", DateTime.Now.ToString(DateFormat, CultureInfo.InvariantCulture)), ("@id", phaseId));
        }

        private void Exec(string sql, params (string, object?)[] prms)
        {
            using var c = CreateConnection();
            using var cmd = new SQLiteCommand(sql, c);
            foreach (var (k, v) in prms) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    public class TimeRecordData
    {
        public int Id { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public string? WindowTitle { get; set; }
        public long UsageTime { get; set; }
        public string Date { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public int? CategoryId { get; set; }
        public bool IsForeground { get; set; }
        public int? ActivityId { get; set; }
        public int? UserId { get; set; }
        public string? CategoryName { get; set; }
    }

    public class ActivityData
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Color { get; set; } = "#6c5ce7";
        public string Icon { get; set; } = "\U0001f4cc";
    }

    public class CategoryData
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Color { get; set; } = "#3498db";
        public string Description { get; set; } = string.Empty;
    }

    public class ProcessUsageData
    {
        public string ProcessName { get; set; } = string.Empty;
        public long TotalUsage { get; set; }
        public string CategoryName { get; set; } = string.Empty;
    }

    public class CategoryStatsData
    {
        public string CategoryName { get; set; } = string.Empty;
        public long TotalUsage { get; set; }
    }

    public class ForegroundStatsData
    {
        public string Type { get; set; } = string.Empty;
        public long TotalUsage { get; set; }
    }

    public class ActivityUsageData
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Color { get; set; } = "#6c5ce7";
        public long TotalUsage { get; set; }
    }

    public class TodoItemData
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsCompleted { get; set; }
        public int Priority { get; set; } = 1;
        public string? DueDate { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
        public string? CompletedAt { get; set; }
        public string DeviceId { get; set; } = string.Empty;
        public int? UserId { get; set; }
    }

    public class ScheduleData
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string StartTime { get; set; } = string.Empty;
        public string? EndTime { get; set; }
        public bool IsAllDay { get; set; }
        public string Color { get; set; } = "#6c5ce7";
        public string CreatedAt { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public int? UserId { get; set; }
    }

    public class ManualRecordData
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string StartTime { get; set; } = string.Empty;
        public int DurationMinutes { get; set; }
        public int? CategoryId { get; set; }
        public int? ActivityId { get; set; }
        public int? UserId { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
    }

    public class GoalData
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = "active";
        public int TotalMinutesGoal { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
        public string? CompletedAt { get; set; }
    }

    public class GoalPhaseData
    {
        public int Id { get; set; }
        public int GoalId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int PhaseOrder { get; set; }
        public int EstimatedMinutes { get; set; }
        public int ActualMinutes { get; set; }
        public string Status { get; set; } = "pending";
        public double EffectiveRatio { get; set; }
        public string UserNotes { get; set; } = string.Empty;
        public string CreatedAt { get; set; } = string.Empty;
        public string? CompletedAt { get; set; }
    }
}
