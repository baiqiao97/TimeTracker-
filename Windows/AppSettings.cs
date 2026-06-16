using System.IO;
using System.Text.Json;

namespace TimeTracker
{
    /// <summary>
    /// 全局应用设置（单例，持久化到 JSON）
    /// </summary>
    public static class AppSettings
    {
        public static bool IsPortable { get; private set; } = false;
        private static string ConfigDir => IsPortable
            ? AppDomain.CurrentDomain.BaseDirectory
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeTracker");
        private static string ConfigFile => Path.Combine(ConfigDir, "settings.json");

        public static void InitPortable(string[] args)
        {
            if (args.Any(a => a.Equals("--portable", StringComparison.OrdinalIgnoreCase)))
                IsPortable = true;
        }

        public static int TrackingIntervalSeconds { get; set; } = 2;
        public static int RetentionDays { get; set; } = 90;
        public static bool AutoStart { get; set; } = false;
        public static string TrackingMode { get; set; } = "simple";
        private static readonly object _activityLock = new();
        private static int? _currentActivityId;
        public static int? CurrentActivityId
        {
            get { lock (_activityLock) return _currentActivityId; }
            set { lock (_activityLock) _currentActivityId = value; }
        }
        public static string ServerUrl { get; set; } = "";
        public static string AuthToken { get; set; } = "";
        public static bool AutoSync { get; set; } = false;
        public static bool HostServer { get; set; } = false;
        public static int ServerPort { get; set; } = 5080;
        public static int MinPasswordLength { get; set; } = 6;
        public static bool DarkMode { get; set; } = false;
        public static int DailyLimitMinutes { get; set; } = 0;
        public static bool AutoExportEnabled { get; set; } = false;
        public static int AutoExportIntervalMinutes { get; set; } = 1440;
        public static string DatabasePassword { get; set; } = "";
        public static string AiApiKey { get; set; } = "";
        public static string AiProviderRaw { get; set; } = "OpenAI";
        public static AIService.Provider AiProvider => AiProviderRaw == "Anthropic" ? AIService.Provider.Anthropic : AIService.Provider.OpenAI;
        public static DateTime LastSyncTime { get; set; } = DateTime.MinValue;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        static AppSettings() => Load();

        public static void Load()
        {
            try
            {
                if (File.Exists(ConfigFile))
                {
                    var json = File.ReadAllText(ConfigFile);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("interval", out var i)) TrackingIntervalSeconds = Math.Max(1, i.GetInt32());
                    if (root.TryGetProperty("retention", out var d)) RetentionDays = Math.Max(1, d.GetInt32());
                    if (root.TryGetProperty("autoStart", out var a)) AutoStart = a.GetBoolean();
                    if (root.TryGetProperty("trackingMode", out var m)) TrackingMode = m.GetString() ?? "simple";
                    if (root.TryGetProperty("currentActivityId", out var aid) && aid.ValueKind != JsonValueKind.Null)
                        CurrentActivityId = aid.GetInt32();
                    if (root.TryGetProperty("serverUrl", out var s)) ServerUrl = s.GetString() ?? "";
                    if (root.TryGetProperty("autoSync", out var a2)) AutoSync = a2.GetBoolean();
                    if (root.TryGetProperty("authToken", out var at)) AuthToken = at.GetString() ?? "";
                    if (root.TryGetProperty("hostServer", out var h)) HostServer = h.GetBoolean();
                    if (root.TryGetProperty("serverPort", out var sp)) ServerPort = sp.GetInt32();
                    if (root.TryGetProperty("minPasswordLength", out var ml)) MinPasswordLength = Math.Max(1, ml.GetInt32());
                    if (root.TryGetProperty("darkMode", out var dm)) DarkMode = dm.GetBoolean();
                    if (root.TryGetProperty("aiApiKey", out var ai)) AiApiKey = ai.GetString() ?? "";
                    if (root.TryGetProperty("aiProviderRaw", out var pr)) AiProviderRaw = pr.GetString() ?? "OpenAI";
                    if (root.TryGetProperty("databasePassword", out var dp)) DatabasePassword = dp.GetString() ?? "";
                    if (root.TryGetProperty("autoExportEnabled", out var ae)) AutoExportEnabled = ae.GetBoolean();
                    if (root.TryGetProperty("autoExportIntervalMinutes", out var aei)) AutoExportIntervalMinutes = aei.GetInt32();
                    if (root.TryGetProperty("lastSyncTime", out var ls) && ls.ValueKind != JsonValueKind.Null)
                    {
                        var ts = ls.GetString();
                        if (!string.IsNullOrEmpty(ts) && DateTime.TryParse(ts, out var dt))
                            LastSyncTime = dt;
                        else
                            LastSyncTime = DateTime.MinValue;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to load settings, using defaults", ex);
            }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                var json = JsonSerializer.Serialize(new
                {
                    interval = TrackingIntervalSeconds,
                    retention = RetentionDays,
                    autoStart = AutoStart,
                    trackingMode = TrackingMode,
                    currentActivityId = CurrentActivityId,
                    serverUrl = ServerUrl,
                    autoSync = AutoSync,
                    authToken = AuthToken,
                    hostServer = HostServer,
                    serverPort = ServerPort,
                    minPasswordLength = MinPasswordLength,
                    darkMode = DarkMode
                }, _jsonOptions);
                // 修复：原子写入，写临时文件再重命名，防止崩溃丢失配置
                var tempFile = ConfigFile + ".tmp";
                File.WriteAllText(tempFile, json);
                File.Move(tempFile, ConfigFile, overwrite: true);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to save settings", ex);
            }
        }

        /// <summary>
        /// 开机自启：写入/删除注册表 Run 键
        /// </summary>
        public static void ApplyAutoStart()
        {
            try
            {
                var rk = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                if (rk == null) return;

                if (AutoStart)
                {
                    var exePath = Environment.ProcessPath ?? Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory, "TimeTracker.exe");
                    rk.SetValue("TimeTracker", $"\"{exePath}\" --minimized");
                }
                else
                {
                    rk.DeleteValue("TimeTracker", false);
                }
                rk.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AutoStart registry error: {ex.Message}");
            }
        }
    }
}
