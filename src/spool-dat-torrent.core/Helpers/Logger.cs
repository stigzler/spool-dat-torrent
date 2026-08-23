using System;
using System.IO;

namespace SpoolDatTorrent.Core.Helpers
{
    public static class Logger
    {
        private static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SpoolDatTorrent.log");
        private static readonly object _lock = new object();

        public static void Clear()
        {
            lock (_lock)
            {
                File.WriteAllText(LogPath, string.Empty);
            }
        }

        public static void Log(string message, bool echoToConsole = false)
        {
            lock (_lock)
            {
                var timestamped = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";

                if (echoToConsole)
                {
                    Console.WriteLine(timestamped);
                }

                File.AppendAllText(LogPath, timestamped + Environment.NewLine);
            }
        }
    }
}