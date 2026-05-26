using System.IO;
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

                var json = JsonSerializer.Serialize(records, JsonOptions);
                File.WriteAllText(path, json);
                return (true, records.Count, null);
            }
            catch (Exception ex)
            {
                return (false, 0, ex.Message);
            }
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

                var json = File.ReadAllText(path);
                var records = JsonSerializer.Deserialize<List<TimeRecordData>>(json);

                if (records == null || records.Count == 0)
                    return (false, 0, 0, "文件中没有记录");

                var existingKeys = databaseManager.GetAllRecordKeys();
                var newRecords = new List<TimeRecordData>();

                foreach (var record in records)
                {
                    if (!DateTime.TryParse(record.Date, out var recordDate))
                        continue;
                    var key = $"{recordDate:yyyyMMdd}|{record.ProcessName}|{record.DeviceId}";
                    if (!existingKeys.Contains(key))
                        newRecords.Add(record);
                }

                if (newRecords.Count > 0)
                    databaseManager.InsertTimeRecords(newRecords);

                return (true, newRecords.Count, records.Count - newRecords.Count, null);
            }
            catch (Exception ex)
            {
                return (false, 0, 0, ex.Message);
            }
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

