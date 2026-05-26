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
            _dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dbPath);
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
                    "CREATE TABLE IF NOT EXISTS users (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, device_id TEXT NOT NULL)",
                    "CREATE TABLE IF NOT EXISTS activities (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, color TEXT DEFAULT '#6c5ce7', icon TEXT DEFAULT '📌')"
                ];

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
            using var connection = CreateConnection();
            using var transaction = connection.BeginTransaction();
            try
            {
                const string insertQuery = @"
                    INSERT INTO time_records (process_name, window_title, usage_time, date, device_id, category_id, is_foreground, activity_id, user_id)
                    VALUES (@processName, @windowTitle, @usageTime, @date, @deviceId, @categoryId, @isForeground, @activityId, @userId)
                ";

                using var command = new SQLiteCommand(insertQuery, connection, transaction);
                foreach (var r in records)
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

        public void AddActivity(string name, string color = "#6c5ce7", string icon = "📌")
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
        public string Icon { get; set; } = "📌";
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
}
