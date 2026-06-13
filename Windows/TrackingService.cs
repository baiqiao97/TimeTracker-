using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace TimeTracker
{
    public class TrackingService : IDisposable
    {
        // 空闲检测 P/Invoke
        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        private static uint GetIdleSeconds()
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            GetLastInputInfo(ref info);
            // 修复：使用 unchecked 处理 TickCount 回绕溢出
            unchecked { return (uint)Environment.TickCount - info.dwTime; }
        }

        private const int IdleThresholdSeconds = 300; // 5分钟无操作自动暂停
        private DateTime _idlePauseSince;
        private bool _idlePaused;
        private DateTime _idleSuppressUntil = DateTime.MinValue;
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly Task _trackingTask;
        private string _currentProcessName = string.Empty;
        private string _currentWindowTitle = string.Empty;
        private DateTime _lastActivityTime;
        private readonly ConcurrentDictionary<string, long> _processUsageTime;
        private readonly ConcurrentDictionary<string, PendingRecord> _pendingDbWrite;
        private readonly DatabaseManager _databaseManager;
        private readonly string _deviceId;
        private readonly Dictionary<string, int> _processCategoryMap;
        private readonly object _categoryMapLock = new();
        private readonly object _flushLock = new(); // 修复：FlushPendingWrites 线程同步锁
        private volatile bool _isPaused;
        private readonly int _pollingIntervalMs;

        public bool IsPaused => _isPaused || _idlePaused;
        public event Action<string>? StatusUpdated;
        public event Action<bool>? PauseStateChanged;

        public TrackingService(DatabaseManager databaseManager, int intervalSeconds = 2)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _processUsageTime = new ConcurrentDictionary<string, long>();
            _pendingDbWrite = new ConcurrentDictionary<string, PendingRecord>();
            _processCategoryMap = new Dictionary<string, int>();
            _lastActivityTime = DateTime.Now;
            _databaseManager = databaseManager;
            _deviceId = GetDeviceId();
            _pollingIntervalMs = Math.Max(500, intervalSeconds * 1000);
            LoadProcessCategoryMap();

            _trackingTask = Task.Run(() => TrackWindowActivity(_cancellationTokenSource!.Token));
        }

        public void Dispose()
        {
            Stop();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }

        public void Stop()
        {
            _cancellationTokenSource?.Cancel();
            try
            {
                if (!_trackingTask.Wait(TimeSpan.FromSeconds(5)))
                    Logger.Warn("Tracking task did not stop in time");
            }
            catch (AggregateException ex)
            {
                Logger.Error("Tracking task stopped with error", ex.InnerException);
            }
            FlushPendingWrites();
        }

        public void Pause()
        {
            if (_isPaused) return;
            _isPaused = true;
            _idlePaused = false; // 手动暂停时清除空闲状态
            FlushPendingWrites();
            PauseStateChanged?.Invoke(true);
            StatusUpdated?.Invoke("追踪已暂停");
        }

        public void Resume()
        {
            if (!_isPaused && !_idlePaused) return;
            _isPaused = false;
            _idlePaused = false;
            _idleSuppressUntil = DateTime.Now.AddSeconds(5); // 手动恢复后5秒内不触发空闲检测
            _lastActivityTime = DateTime.Now;
            _currentProcessName = string.Empty;
            _currentWindowTitle = string.Empty;
            PauseStateChanged?.Invoke(false);
            StatusUpdated?.Invoke("追踪已恢复");
        }

        public void TogglePause()
        {
            if (IsPaused) Resume();
            else Pause();
        }

        public Dictionary<string, long> GetProcessUsageTime()
        {
            // ConcurrentDictionary 的 ToDictionary 是线程安全的快照
            return new Dictionary<string, long>(_processUsageTime);
        }

        private void LoadProcessCategoryMap()
        {
            try
            {
                lock (_categoryMapLock)
                {
                    _processCategoryMap.Clear();

                    var categories = _databaseManager.GetCategories();
                    var defaultMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["chrome"] = "浏览器",
                        ["firefox"] = "浏览器",
                        ["edge"] = "浏览器",
                        ["msedge"] = "浏览器",
                        ["code"] = "工具",
                        ["visualstudio"] = "工具",
                        ["devenv"] = "工具",
                        ["notepad++"] = "工具",
                        ["notepad"] = "工具",
                        ["word"] = "办公软件",
                        ["excel"] = "办公软件",
                        ["powerpoint"] = "办公软件",
                        ["winword"] = "办公软件",
                        ["steam"] = "游戏",
                        ["discord"] = "通讯",
                        ["wechat"] = "通讯",
                        ["qq"] = "通讯",
                        ["whatsapp"] = "社交媒体",
                        ["youtube"] = "视频",
                    };

                    foreach (var cat in categories)
                    {
                        foreach (var kvp in defaultMappings)
                        {
                            if (string.Equals(cat.Name, kvp.Value, StringComparison.OrdinalIgnoreCase))
                            {
                                _processCategoryMap[kvp.Key] = cat.Id;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Error loading category map", ex);
            }
        }

        public void AddProcessCategoryMapping(string processName, int categoryId)
        {
            lock (_categoryMapLock)
            {
                _processCategoryMap[processName.ToLowerInvariant()] = categoryId;
            }
        }

        private async Task TrackWindowActivity(CancellationToken cancellationToken)
        {
            string lastWindowTitle = string.Empty;
            while (!cancellationToken.IsCancellationRequested)
            {
                // 空闲检测：5分钟无操作自动暂停，恢复操作自动恢复
                if (!_isPaused || _idlePaused)
                {
                    var now = DateTime.Now;
                    if (now >= _idleSuppressUntil)
                    {
                        // 修复：GetIdleSeconds 返回的是毫秒差值（unchecked），需要与阈值比较
                        var idleMs = GetIdleSeconds();
                        if (idleMs >= (uint)(IdleThresholdSeconds * 1000) && !_idlePaused)
                        {
                            _idlePaused = true;
                            _idlePauseSince = now;
                            StatusUpdated?.Invoke("空闲暂停 (5分钟无操作)");
                            PauseStateChanged?.Invoke(true);
                        }
                        else if (idleMs < (uint)(IdleThresholdSeconds * 1000) && _idlePaused)
                        {
                            _idlePaused = false;
                            StatusUpdated?.Invoke("追踪已恢复");
                            PauseStateChanged?.Invoke(false);
                        }
                    }
                }

                if (!_isPaused && !_idlePaused)
                {
                    try
                    {
                        var windowInfo = GetActiveWindowInfo();
                        if (windowInfo != null && !string.Equals(windowInfo.WindowTitle, lastWindowTitle, StringComparison.Ordinal))
                        {
                            UpdateProcessUsage(windowInfo, true);
                            StatusUpdated?.Invoke($"追踪中: {windowInfo.WindowTitle}");
                            lastWindowTitle = windowInfo.WindowTitle;
                        }
                        else if (windowInfo == null)
                        {
                            UpdateBackgroundProcessUsage();
                        }
                    }
                    catch (Exception ex)
                    {
                        StatusUpdated?.Invoke($"Error: {ex.Message}");
                    }
                }

                try
                {
                    await Task.Delay(_pollingIntervalMs, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private static WindowInfo? GetActiveWindowInfo()
        {
            try
            {
                IntPtr hWnd = GetForegroundWindow();
                if (hWnd == IntPtr.Zero)
                    return null;

                GetWindowThreadProcessId(hWnd, out int processId);

                Process? process = null;
                try
                {
                    process = Process.GetProcessById(processId);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Process lookup error for pid {processId}", ex);
                    return null;
                }

                if (process == null)
                    return null;

                using (process) // 确保 Dispose 释放系统句柄
                {
                    var windowTitleBuilder = new StringBuilder(256);
                    GetWindowText(hWnd, windowTitleBuilder, 256);
                    string windowTitle = windowTitleBuilder.ToString();

                    return new WindowInfo
                    {
                        ProcessId = processId,
                        ProcessName = process.ProcessName,
                        WindowTitle = windowTitle
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.Error("GetActiveWindowInfo error", ex);
                return null;
            }
        }

        // 仅在 TrackWindowActivity 单线程循环中访问
        private int _dbWriteCounter;
        private const int DB_WRITE_INTERVAL = 5;

        private void UpdateProcessUsage(WindowInfo windowInfo, bool isForeground)
        {
            try
            {
                DateTime currentTime = DateTime.Now;
                TimeSpan elapsedTime = currentTime - _lastActivityTime;

                if (!string.IsNullOrEmpty(_currentProcessName))
                {
                    long usageTime = (long)elapsedTime.TotalMilliseconds;

                    _processUsageTime.AddOrUpdate(_currentProcessName, usageTime, (_, old) => old + usageTime);

                    _pendingDbWrite.AddOrUpdate(
                        _currentProcessName,
                        new PendingRecord { UsageTime = usageTime, WindowTitle = _currentWindowTitle },
                        (_, existing) =>
                        {
                            existing.UsageTime += usageTime;
                            // 保留最新的窗口标题
                            existing.WindowTitle = _currentWindowTitle;
                            return existing;
                        });

                    _dbWriteCounter++;
                    if (_dbWriteCounter >= DB_WRITE_INTERVAL)
                    {
                        FlushPendingWrites(isForeground);
                        _dbWriteCounter = 0;
                    }
                }

                _currentProcessName = windowInfo.ProcessName;
                _currentWindowTitle = windowInfo.WindowTitle;
                _lastActivityTime = currentTime;
            }
            catch (Exception ex)
            {
                Logger.Error("UpdateProcessUsage error", ex);
            }
        }

        private void UpdateBackgroundProcessUsage()
        {
            try
            {
                DateTime currentTime = DateTime.Now;
                TimeSpan elapsedTime = currentTime - _lastActivityTime;

                if (!string.IsNullOrEmpty(_currentProcessName))
                {
                    long usageTime = (long)elapsedTime.TotalMilliseconds;

                    _processUsageTime.AddOrUpdate(_currentProcessName, usageTime, (_, old) => old + usageTime);

                    int? categoryId = GetCategoryIdForProcess(_currentProcessName);
                    try
                    {
                        _databaseManager.InsertTimeRecord(
                            _currentProcessName, _currentWindowTitle, usageTime,
                            _deviceId, categoryId, false,
                            AppSettings.CurrentActivityId);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Database insert error", ex);
                    }
                }

                _currentProcessName = "Background";
                _currentWindowTitle = "Background";
                _lastActivityTime = currentTime;
            }
            catch (Exception ex)
            {
                Logger.Error("UpdateBackgroundProcessUsage error", ex);
            }
        }

        private void FlushPendingWrites(bool isForeground = true)
        {
            // 修复：加锁防止与 Stop() 中的 FlushPendingWrites() 并发竞争
            lock (_flushLock)
            {
                try
                {
                    if (_pendingDbWrite.IsEmpty) return;

                    var records = new List<TimeRecordData>();
                    foreach (var kvp in _pendingDbWrite)
                    {
                        if (kvp.Value.UsageTime <= 0) continue;
                        int? categoryId = GetCategoryIdForProcess(kvp.Key);
                        records.Add(new TimeRecordData
                        {
                            ProcessName = kvp.Key,
                            WindowTitle = kvp.Value.WindowTitle,
                            UsageTime = kvp.Value.UsageTime,
                            Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                            DeviceId = _deviceId,
                            CategoryId = categoryId,
                            IsForeground = isForeground,
                            ActivityId = AppSettings.CurrentActivityId
                        });
                    }
                    _pendingDbWrite.Clear();

                    if (records.Count > 0)
                        _databaseManager.InsertTimeRecords(records);
                }
                catch (Exception ex)
                {
                    Logger.Error("FlushPendingWrites error", ex);
                }
            }
        }

        private int? GetCategoryIdForProcess(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return null;
            lock (_categoryMapLock)
            {
                if (_processCategoryMap.TryGetValue(processName.ToLowerInvariant(), out int categoryId))
                {
                    return categoryId;
                }
            }
            return null;
        }

        private static string GetDeviceId()
        {
            return Environment.MachineName + "-" + Environment.OSVersion.Version.ToString();
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    }

    /// <summary>
    /// 待写入数据库的记录，包含每个进程自身的窗口标题
    /// </summary>
    internal sealed class PendingRecord
    {
        public long UsageTime { get; set; }
        public string WindowTitle { get; set; } = string.Empty;
    }

    public class WindowInfo
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public string WindowTitle { get; set; } = string.Empty;
    }
}
