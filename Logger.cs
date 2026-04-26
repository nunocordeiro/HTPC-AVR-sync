using System;
using System.IO;
using System.Reflection;

namespace HTPCAVRVolume
{
    internal static class Logger
    {
        private static readonly string _logPath =
            Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "HTPCAVRVolume.log");

        private static readonly object _lock = new object();

        public static void Log(string message)
        {
            try
            {
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}";
                lock (_lock)
                    File.AppendAllText(_logPath, line + Environment.NewLine);
            }
            catch { /* logging must never crash the app */ }
        }

        public static void LogException(string context, Exception ex)
        {
            Log($"ERROR in {context}: {ex.GetType().Name}: {ex.Message}");
            Log($"  Stack: {ex.StackTrace?.Replace(Environment.NewLine, " | ")}");
            if (ex.InnerException != null)
                Log($"  Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
        }

        /// <summary>Rolls the log over 1 MB so it never grows unbounded.</summary>
        public static void Init()
        {
            try
            {
                if (File.Exists(_logPath) && new FileInfo(_logPath).Length > 1_000_000)
                    File.Delete(_logPath);
                Log("=== App started ===");
            }
            catch { }
        }
    }
}
