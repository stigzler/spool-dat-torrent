using System;
using System.Collections.Generic;
using System.Linq;
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
                return Path.Combine(envPath, "config.json");
            }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        }

        /// <summary>
        /// Resolve the directory where per-stream source files (.torrent/.dat) are cached.
        /// Priority: explicit <paramref name="configuredPath"/> > SPOOL_CACHE_DIR env var >
        /// a "cache" folder beside the settings file. The directory is created if missing.
        /// </summary>
        public static string GetCacheDirectory(string configuredPath)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                Directory.CreateDirectory(configuredPath);
                return configuredPath;
            }

            var envPath = Environment.GetEnvironmentVariable("SPOOL_CACHE_DIR");
            if (!string.IsNullOrWhiteSpace(envPath))
            {
                Directory.CreateDirectory(envPath);
                return envPath;
            }

            var defaultPath = Path.Combine(Path.GetDirectoryName(GetSettingsPath()) ?? AppDomain.CurrentDomain.BaseDirectory, "cache");
            Directory.CreateDirectory(defaultPath);
            return defaultPath;
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

        /// <summary>
        /// Add a new server profile to the settings file (creating a default placeholder
        /// profile) and persist it. Returns the name of the created profile.
        /// </summary>
        public static string AddServerProfile()
        {
            string settingsPath = GetSettingsPath();

            GlobalSpoolSettings settings;
            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                settings = JsonSerializer.Deserialize<GlobalSpoolSettings>(json) ?? new GlobalSpoolSettings();
            }
            else
            {
                settings = new GlobalSpoolSettings
                {
                    DefaultServerProfile = "LocalQBit",
                    DefaultSpoolingTarget = @"/downloads/spooled",
                    PollIntervalSeconds = 15,
                    SettlingTimeSeconds = 30,
                    TorrentServers = new Dictionary<string, TorrentServerProfile>()
                };
            }

            // Generate a unique name: "New Server" + 4 random characters.
            var random = new Random();
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // avoid 0/O, 1/I confusion
            var suffix = new string(Enumerable.Repeat(chars, 4).Select(s => s[random.Next(s.Length)]).ToArray());
            var profileName = $"New Server {suffix}";

            settings.TorrentServers[profileName] = new TorrentServerProfile
            {
                ClientType = "ToBeSet",
                Host = "ToBeSet",
                Username = string.Empty,
                Password = string.Empty,
                ApiKey = string.Empty,
                SpoolingCapGb = 500,
                ClientDownloadsMapping = new ClientDownloadsMapping
                {
                    ClientVirtualPrefix = string.Empty,
                    AppVirtualPrefix = string.Empty
                }
            };

            var writeOptions = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings, writeOptions));

            return profileName;
        }

        /// <summary>
        /// Remove a server profile by name from the settings file and persist the change.
        /// </summary>
        /// <returns>True if the profile existed and was removed; false otherwise.</returns>
        public static bool DeleteServerProfile(string profileName)
        {
            string settingsPath = GetSettingsPath();
            if (!File.Exists(settingsPath))
            {
                return false;
            }

            var json = File.ReadAllText(settingsPath);
            var settings = JsonSerializer.Deserialize<GlobalSpoolSettings>(json) ?? new GlobalSpoolSettings();

            if (!settings.TorrentServers.Remove(profileName))
            {
                return false;
            }

            // If the deleted profile was the default, clear the default so it doesn't
            // reference a now-missing profile.
            if (string.Equals(settings.DefaultServerProfile, profileName, StringComparison.OrdinalIgnoreCase))
            {
                settings.DefaultServerProfile = settings.TorrentServers.Keys.FirstOrDefault() ?? string.Empty;
            }

            var writeOptions = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings, writeOptions));

            return true;
        }
    }
}
