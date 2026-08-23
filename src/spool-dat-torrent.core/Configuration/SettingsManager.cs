using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace SpoolDatTorrent.Core.Configuration
{
    public static class SettingsManager
    {
        public static string GetSettingsPath()
        {
            var envPath = Environment.GetEnvironmentVariable("SPOOL_CONFIG_DIR");

            if (!string.IsNullOrWhiteSpace(envPath))
            {
                Directory.CreateDirectory(envPath);
                return Path.Combine(envPath, "spool_settings.json");
            }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "spool_settings.json");
        }

        public static void EnsureDefaultSettingsExist()
        {
            string settingsPath = GetSettingsPath();

            if (File.Exists(settingsPath)) return;

            var defaultSettings = new GlobalSpoolSettings
            {
                DefaultServerProfile = "LocalQBit",
                DefaultSpoolingTarget = @"/downloads/spooled",
                PollIntervalSeconds = 15,
                SettlingTimeSeconds = 30,
                TorrentServers = new Dictionary<string, TorrentServerProfile>
                {
                    {
                        "LocalQBit",
                        new TorrentServerProfile
                        {
                            ClientType = "qBittorrent",
                            Host = "http://localhost:8080",
                            Username = "admin",
                            Password = "",
                            ApiKey = "",
                            SpoolingCapGb = 500,
                            ClientDownloadsMapping = new ClientDownloadsMapping
                            {
                                ClientVirtualPrefix = "",
                                AppVirtualPrefix = ""
                            }
                        }
                    }
                }
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(defaultSettings, options);

            File.WriteAllText(settingsPath, json);
            Console.WriteLine($"[Init] Created default configuration file at: {settingsPath}");
            Console.WriteLine("[Init] Please update your server configuration and restart.");
            Environment.Exit(0);
        }
    }
}
