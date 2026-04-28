using System;
using System.IO;
using System.Reflection;

namespace HTPCAVRVolume
{
    internal static class Logger
    {
        private static readonly string _logPath =
            Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "HTPCAVRVolume.log");
        private static readonly string _bakPath =
            Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "HTPCAVRVolume.log.bak");

        private static readonly object _lock = new object();

        // Trim check: every 500 writes, trim if file exceeds 500 KB.
        private const int  TrimCheckInterval = 500;
        private const long TrimThresholdBytes = 500_000;
        private const int  TrimKeepLines      = 1000;
        private static int _writeCount;

        public static void Log(string message)
        {
            try
            {
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}";
                lock (_lock)
                {
                    File.AppendAllText(_logPath, line + Environment.NewLine);

                    if (++_writeCount % TrimCheckInterval == 0)
                        TrimIfNeeded();
                }
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

        /// <summary>
        /// On startup: if the log exceeds 200 KB, rotate it to .log.bak and start fresh.
        /// Call once from Program.Main before anything else logs.
        /// </summary>
        public static void Init()
        {
            try
            {
                if (File.Exists(_logPath) && new FileInfo(_logPath).Length > 200_000)
                {
                    File.Copy(_logPath, _bakPath, overwrite: true);
                    File.Delete(_logPath);
                }
                Log("=== App started ===");
            }
            catch { }
        }

        // Caller must hold _lock.
        private static void TrimIfNeeded()
        {
            try
            {
                if (!File.Exists(_logPath)) return;
                if (new FileInfo(_logPath).Length <= TrimThresholdBytes) return;

                string[] lines = File.ReadAllLines(_logPath);
                if (lines.Length <= TrimKeepLines) return;

                // Keep only the most recent TrimKeepLines lines.
                string[] kept = new string[TrimKeepLines];
                Array.Copy(lines, lines.Length - TrimKeepLines, kept, 0, TrimKeepLines);
                File.WriteAllLines(_logPath, kept);
            }
            catch { }
        }
    }
}
