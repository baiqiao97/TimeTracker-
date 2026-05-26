using System.IO;

namespace TimeTracker
{
    public static class Logger
    {
        private static readonly string LogDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeTracker");
        private static readonly string LogFile = Path.Combine(LogDir, "timetracker.log");

        static Logger()
        {
            try { Directory.CreateDirectory(LogDir); } catch { }
        }

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Warn(string message)
        {
            Write("WARN", message);
        }

        public static void Error(string message, Exception? ex = null)
        {
            var fullMessage = ex != null ? $"{message}: {ex}" : message;
            Write("ERROR", fullMessage);
        }

        private static readonly object _lock = new();
        private static void Write(string level, string message)
        {
            lock (_lock)
            {
                try
                {
                    var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}";
                    File.AppendAllText(LogFile, line);
                    System.Diagnostics.Debug.WriteLine(line);
                }
                catch { }
            }
        }
    }
}
