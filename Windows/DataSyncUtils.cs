using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace TimeTracker
{
    public static class DataSyncUtils
    {
        private const string ExportDir = "TimeTracker";
        private const string ExportFile = "time_tracker_data.json";

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        /// <summary>获取默认导出路径</summary>
        public static string DefaultPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), ExportDir, ExportFile);

        /// <summary>导出数据到指定文件路径</summary>
        public static (bool success, int count, string? error) ExportData(
            DatabaseManager databaseManager, string? filePath = null)
        {
            try
            {
                var path = filePath ?? DefaultPath;
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var records = databaseManager.GetTimeRecords(
                    DateTime.Now.AddDays(-90), DateTime.MaxValue);

                if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    ExportCsv(records, path);
                else
                {
                    var json = JsonSerializer.Serialize(records, JsonOptions);
                    File.WriteAllText(path, json);
                }
                return (true, records.Count, null);
            }
            catch (Exception ex)
            {
                return (false, 0, ex.Message);
            }
        }

        /// <summary>CSV 导出</summary>
        private static void ExportCsv(List<TimeRecordData> records, string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("日期,进程名,窗口标题,使用时长(ms),使用时长(可读),设备ID,分类ID,前台");
            foreach (var r in records)
            {
                var duration = TimeSpan.FromMilliseconds(r.UsageTime);
                var readable = duration.TotalHours >= 1
                    ? $"{(int)duration.TotalHours}h{duration.Minutes}m" : $"{duration.Minutes}m{duration.Seconds}s";
                var title = (r.WindowTitle ?? "").Replace("\"", "\"\"", StringComparison.Ordinal);
                sb.AppendLine(CultureInfo.InvariantCulture, $"{r.Date},{r.ProcessName},\"{title}\",{r.UsageTime},{readable},{r.DeviceId},{r.CategoryId},{(r.IsForeground ? 1 : 0)}");
            }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        /// <summary>从指定文件路径导入数据</summary>
        public static (bool success, int newCount, int skipped, string? error) ImportData(
            DatabaseManager databaseManager, string? filePath = null)
        {
            try
            {
                var path = filePath ?? DefaultPath;
                if (!File.Exists(path))
                    return (false, 0, 0, $"文件不存在:\n{path}");

                if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    return ImportCsv(databaseManager, path);

                var json = File.ReadAllText(path);
                var records = JsonSerializer.Deserialize<List<TimeRecordData>>(json);

                if (records == null || records.Count == 0)
                    return (false, 0, 0, "文件中没有记录");

                return InsertUniqueRecords(databaseManager, records);
            }
            catch (Exception ex)
            {
                return (false, 0, 0, ex.Message);
            }
        }

        /// <summary>CSV 导入</summary>
        private static (bool success, int newCount, int skipped, string? error) ImportCsv(
            DatabaseManager databaseManager, string path)
        {
            var records = new List<TimeRecordData>();
            var lines = File.ReadAllLines(path);
            if (lines.Length < 2) return (false, 0, 0, "CSV 文件为空");
            for (int i = 1; i < lines.Length; i++)
            {
                var parts = lines[i].Split(',');
                if (parts.Length < 5) continue;
                var record = new TimeRecordData
                {
                    Date = parts[0],
                    ProcessName = parts[1],
                    WindowTitle = parts[2].Trim('"'),
                    UsageTime = long.TryParse(parts[3], out var t) ? t : 0,
                    DeviceId = parts.Length > 4 ? parts[4] : "",
                    CategoryId = parts.Length > 5 && int.TryParse(parts[5], out var cid) ? cid : null,
                    IsForeground = parts.Length > 6 && parts[6] == "1"
                };
                if (record.UsageTime > 0) records.Add(record);
            }
            return InsertUniqueRecords(databaseManager, records);
        }

        private static (bool success, int newCount, int skipped, string? error) InsertUniqueRecords(
            DatabaseManager databaseManager, List<TimeRecordData> records)
        {
            var existingKeys = databaseManager.GetAllRecordKeys();
            var newRecords = new List<TimeRecordData>();
            foreach (var record in records)
            {
                if (!DateTime.TryParse(record.Date, out var recordDate)) continue;
                var key = $"{recordDate:yyyyMMdd}|{record.ProcessName}|{record.DeviceId}";
                if (!existingKeys.Contains(key)) newRecords.Add(record);
            }
            if (newRecords.Count > 0) databaseManager.InsertTimeRecords(newRecords);
            return (true, newRecords.Count, records.Count - newRecords.Count, null);
        }

        /// <summary>同步数据：先导出再导入</summary>
        public static (bool success, string? error) SyncData(DatabaseManager databaseManager)
        {
            try
            {
                var (exportOk, _, _) = ExportData(databaseManager);
                if (!exportOk)
                    return (false, "导出本地数据失败");

                var (_, _, _, importErr) = ImportData(databaseManager);
                return importErr == null ? (true, null) : (false, importErr);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}

