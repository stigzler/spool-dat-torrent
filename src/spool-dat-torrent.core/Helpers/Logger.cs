using SpoolDatTorrent.Core.Configuration;
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace SpoolDatTorrent.Core.Helpers
{
    /// <summary>
    /// Lightweight, host-agnostic logger shared by the CLI, web UI, and (future) desktop app.
    /// Writes every line to a durable log file and, optionally, echoes it to stdout so
    /// Docker / Dockge capture it via <c>docker logs</c>.
    ///
    /// Two levels are supported:
    ///   - <see cref="Log"/> / <see cref="LogWarning"/> / <see cref="LogError"/> — the tidy,
    ///     uncluttered "standard" log (startup/shutdown, settings, command receipt, API
    ///     errors, process outcomes).
    ///   - <see cref="LogDebug"/> — verbose detail for troubleshooting, gated behind the
    ///     <c>SDT_DEBUG_LOG=1</c> environment variable.
    ///
    /// Caller info (<c>[Class.Method]</c>) is appended to debug lines and error lines so the
    /// origin is easy to pinpoint, but is omitted from normal info lines to keep them tidy.
    /// </summary>
    public static class Logger
    {
        private static readonly object _lock = new object();

        private static readonly string LogDirectory;
        private static readonly string LogPath;

        // Rotation: archive to a dated file when the day changes, and size-rotate within a
        // day so a long-running Docker container never grows the log unbounded.
        private const long MaxFileSizeBytes = 5L * 1024 * 1024; // 5 MB
        private const int MaxSizeBackups = 3;
        private static string _currentDay;

        /// <summary>
        /// When true, <see cref="LogDebug"/> writes to the log. Toggled via the
        /// <c>SDT_DEBUG_LOG=1</c> environment variable for troubleshooting.
        /// </summary>
        public static bool IsDebugEnabled { get; set; } =
            Environment.GetEnvironmentVariable("SDT_DEBUG_LOG") == "1";

        /// <summary>
        /// When true, every line is also written to stdout. Defaults to true so Docker/web
        /// capture the log; the CLI sets this to false because Spectre.Console owns the
        /// console and raw writes would corrupt its live display.
        /// </summary>
        public static bool EchoToConsole { get; set; } = true;

        static Logger()
        {
            // Put the log beside config.json so it persists on the mounted data volume in
            // Docker (SPOOL_CONFIG_DIR), and in the app base directory otherwise.
            var settingsDir = Path.GetDirectoryName(SettingsManager.GetSettingsPath())
                ?? AppDomain.CurrentDomain.BaseDirectory;
            LogDirectory = settingsDir;
            LogPath = Path.Combine(LogDirectory, "SpoolDatTorrent.log");
            _currentDay = DateTime.Now.ToString("yyyy-MM-dd");
        }

        /// <summary>Writes a standard (Info) line. No caller info is appended.</summary>
        public static void Log(string message,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string sourceFile = "")
        {
            Write("INFO", message, includeCaller: false, memberName, sourceFile);
        }

        /// <summary>Writes a warning line. No caller info is appended.</summary>
        public static void LogWarning(string message,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string sourceFile = "")
        {
            Write("WARN", message, includeCaller: false, memberName, sourceFile);
        }

        /// <summary>Writes an error line, appending <c>[Class.Method]</c> to aid diagnosis.</summary>
        public static void LogError(string message,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string sourceFile = "")
        {
            Write("ERR!", message, includeCaller: true, memberName, sourceFile);
        }

        /// <summary>
        /// Writes a debug line (only when <see cref="IsDebugEnabled"/>), appending
        /// <c>[Class.Method]</c> to aid diagnosis.
        /// </summary>
        public static void LogDebug(string message,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string sourceFile = "")
        {
            if (!IsDebugEnabled)
            {
                return;
            }

            Write("DEBUG", message, includeCaller: true, memberName, sourceFile);
        }

        /// <summary>Clears the current log file (used by the CLI to start a fresh session).</summary>
        public static void Clear()
        {
            lock (_lock)
            {
                try
                {
                    File.WriteAllText(LogPath, string.Empty, Encoding.UTF8);
                }
                catch
                {
                    // Logging must never throw.
                }
            }
        }

        private static void Write(string level, string message, bool includeCaller,
            string memberName, string sourceFile)
        {
            var now = DateTime.Now;
            string timestamp = level == "DEBUG"
                ? now.ToString("dd-MM-yy HH:mm:ss.fff")
                : now.ToString("dd-MM-yy HH:mm:ss");

            string caller = includeCaller && !string.IsNullOrEmpty(memberName)
                ? $" [{Path.GetFileNameWithoutExtension(sourceFile)}.{memberName}]"
                : string.Empty;

            // Errors get a warning icon right after the level tag so they stand out.
            string icon = level == "ERR!" ? " ⚠️ " : string.Empty;

            string line = $"{timestamp} [{level}] {icon}{caller} {message}";

            lock (_lock)
            {
                RotateIfNeeded(now);

                try
                {
                    File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
                }
                catch
                {
                    // Logging must never throw (e.g. read-only volume).
                }

                if (EchoToConsole)
                {
                    Console.WriteLine(line);
                }
            }
        }

        private static void RotateIfNeeded(DateTime now)
        {
            string day = now.ToString("yyyy-MM-dd");

            // Day changed: archive the current log under the previous day's date and start
            // a fresh file. This keeps a natural per-day history without unbounded growth.
            if (day != _currentDay)
            {
                if (File.Exists(LogPath))
                {
                    string archive = Path.Combine(LogDirectory, $"SpoolDatTorrent-{_currentDay}.log");
                    try
                    {
                        File.Move(LogPath, archive, overwrite: true);
                    }
                    catch
                    {
                        // If the archive move fails, just keep appending to the current file.
                    }
                }

                _currentDay = day;
                return;
            }

            // Same day but the file has grown too large: rotate numbered backups.
            var info = new FileInfo(LogPath);
            if (info.Exists && info.Length > MaxFileSizeBytes)
            {
                RotateSizeBackups();
            }
        }

        private static void RotateSizeBackups()
        {
            try
            {
                // Drop the oldest backup, then shift .2 -> .3, .1 -> .2, current -> .1.
                string oldest = $"{LogPath}.{MaxSizeBackups}";
                if (File.Exists(oldest))
                {
                    File.Delete(oldest);
                }

                for (int i = MaxSizeBackups - 1; i >= 1; i--)
                {
                    string from = $"{LogPath}.{i}";
                    string to = $"{LogPath}.{i + 1}";
                    if (File.Exists(from))
                    {
                        File.Move(from, to, overwrite: true);
                    }
                }

                File.Move(LogPath, $"{LogPath}.1", overwrite: true);
            }
            catch
            {
                // Rotation is best-effort; never let it break logging.
            }
        }
    }
}
