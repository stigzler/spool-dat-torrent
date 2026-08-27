using SpoolDatTorrent.Core.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SpoolDatTorrent.Core.Configuration
{
    public static class SettingsManager
    {
        /// <summary>Shared serializer options, including the client-type enum converter.</summary>
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new BitTorrentClientTypeConverter() }
        };

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
        /// Resolve the SQLite database path. When SPOOL_CONFIG_DIR is set (Docker), the DB
        /// lives beside config.json so it persists on the mounted data volume. Otherwise it
        /// lives in the app base directory (local dev / CLI).
        /// </summary>
        public static string GetDatabasePath()
        {
            var envPath = Environment.GetEnvironmentVariable("SPOOL_CONFIG_DIR");
            if (!string.IsNullOrWhiteSpace(envPath))
            {
                Directory.CreateDirectory(envPath);
                return Path.Combine(envPath, "spooldattorrent.db");
            }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "spooldattorrent.db");
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

        /// <summary>
        /// Load settings from config.json, creating a default config first if missing.
        /// Unlike EnsureDefaultSettingsExist, this never calls Environment.Exit, so it is
        /// safe to call from a long-running host (e.g. the web app).
        /// </summary>
        public static GlobalSpoolSettings LoadSettings()
        {
            string settingsPath = GetSettingsPath();

            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                var settings = JsonSerializer.Deserialize<GlobalSpoolSettings>(json, _jsonOptions) ?? CreateDefaultSettings();
                DecryptSecrets(settings);
                return settings;
            }

            var defaults = CreateDefaultSettings();
            ApplyEnvironmentOverrides(defaults);
            SaveSettings(defaults);
            return defaults;
        }

        /// <summary>
        /// Apply first-boot environment overrides onto a fresh default settings object.
        /// Only runs when config.json is missing, so it never clobbers a user's saved
        /// values. Supports the Docker compose UX where the container-side mount path
        /// is chosen by the user and must become the default destination folder.
        /// </summary>
        private static void ApplyEnvironmentOverrides(GlobalSpoolSettings settings)
        {
            // SDT_SPOOL_DIR: the container-side path the user mounted for the final
            // 1G1R output (e.g. "/dest-dir"). Seeds DefaultSpoolingTarget so the user
            // doesn't have to type it into the web UI Settings page.
            var spoolDir = Environment.GetEnvironmentVariable("SDT_SPOOL_DIR");
            if (!string.IsNullOrWhiteSpace(spoolDir))
            {
                settings.DefaultSpoolingTarget = spoolDir.Trim().TrimEnd('/');
            }
        }

        /// <summary>Persist settings to config.json. Secrets are encrypted before writing.</summary>
        public static void SaveSettings(GlobalSpoolSettings settings)
        {
            string settingsPath = GetSettingsPath();

            // Serialize a deep copy with encrypted secrets so the in-memory object keeps
            // plaintext (the UI/engine read it) and isn't double-encrypted on the next save.
            var copy = JsonSerializer.Deserialize<GlobalSpoolSettings>(JsonSerializer.Serialize(settings, _jsonOptions), _jsonOptions);
            if (copy != null)
            {
                EncryptSecrets(copy);
                File.WriteAllText(settingsPath, JsonSerializer.Serialize(copy, _jsonOptions));
            }
        }

        private static readonly ISecretProtector _protector = new AesSecretProtector();

        /// <summary>Encrypt the secret fields of every server profile before serializing.</summary>
        private static void EncryptSecrets(GlobalSpoolSettings settings)
        {
            foreach (var profile in settings.TorrentServers.Values)
            {
                profile.Username = _protector.Protect(profile.Username);
                profile.Password = _protector.Protect(profile.Password);
                profile.ApiKey = _protector.Protect(profile.ApiKey);
            }
        }

        /// <summary>Decrypt the secret fields of every server profile after deserializing.</summary>
        private static void DecryptSecrets(GlobalSpoolSettings settings)
        {
            foreach (var profile in settings.TorrentServers.Values)
            {
                profile.Username = _protector.Unprotect(profile.Username);
                profile.Password = _protector.Unprotect(profile.Password);
                profile.ApiKey = _protector.Unprotect(profile.ApiKey);
            }
        }

        /// <summary>
        /// Build a fresh settings object. All scalar defaults come from the property
        /// initializers on <see cref="GlobalSpoolSettings"/> (single source of truth);
        /// only the seed server profile is added here.
        /// </summary>
        private static GlobalSpoolSettings CreateDefaultSettings()
        {
            return new GlobalSpoolSettings
            {
                TorrentServers = CreateDefaultServers()
            };
        }

        private static Dictionary<string, TorrentServerProfile> CreateDefaultServers()
        {
            return new Dictionary<string, TorrentServerProfile>
            {
                {
                    "DefaultQBit",
                    new TorrentServerProfile
                    {
                        ClientType = BitTorrentClientType.QBittorrent,
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
            };
        }

        public static void EnsureDefaultSettingsExist()
        {
            string settingsPath = GetSettingsPath();

            if (File.Exists(settingsPath)) return;

            var defaultSettings = CreateDefaultSettings();

            var json = JsonSerializer.Serialize(defaultSettings, _jsonOptions);

            File.WriteAllText(settingsPath, json);
            Logger.Log($"⚙️ Created default configuration file at: {settingsPath}");
            Logger.Log("⚙️ Please update your server configuration and restart.");
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
                settings = JsonSerializer.Deserialize<GlobalSpoolSettings>(json, _jsonOptions) ?? new GlobalSpoolSettings();
            }
            else
            {
                settings = new GlobalSpoolSettings
                {
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
                ClientType = BitTorrentClientType.QBittorrent,
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

            File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings, _jsonOptions));

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
            var settings = JsonSerializer.Deserialize<GlobalSpoolSettings>(json, _jsonOptions) ?? new GlobalSpoolSettings();

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

            File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings, _jsonOptions));

            return true;
        }
    }
}
