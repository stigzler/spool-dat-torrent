using System;
using System.IO;

namespace SpoolDatTorrent.Core.Helpers
{
    public static class Logger
    {
        private static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SpoolDatTorrent.log");
        private static readonly object _lock = new object();

        /// <summary>
        /// When true, <see cref="LogDebug"/> writes to the log. When false, debug lines are
        /// suppressed so the log only contains normal (Info) messages. Toggle via config or
        /// an environment variable for troubleshooting.
        /// </summary>
        public static bool IsDebugEnabled { get; set; } =
            Environment.GetEnvironmentVariable("SDT_DEBUG_LOG") == "1";

        public static void Clear()
        {
            lock (_lock)
            {
                File.WriteAllText(LogPath, string.Empty);
            }
        }

        public static void Log(string message, bool echoToConsole = false)
        {
            Write("INFO", message, echoToConsole);
        }

        public static void LogDebug(string message)
        {
            if (IsDebugEnabled)
            {
                Write("DEBUG", message, echoToConsole: false);
            }
        }

        private static void Write(string level, string message, bool echoToConsole)
        {
            lock (_lock)
            {
                var timestamped = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";

                if (echoToConsole)
                {
                    Console.WriteLine(timestamped);
                }

                File.AppendAllText(LogPath, timestamped + Environment.NewLine);
            }
        }
    }
}
