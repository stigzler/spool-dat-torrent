using SpoolDatTorrent.Core.Configuration;
using SpoolDatTorrent.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SpoolDatTorrent.Core.Helpers
{
    /// <summary>
    /// Emits a tidy, human-readable summary of the app's configuration and current state to
    /// the standard log. Used at startup (web host / CLI) so the operator can see the global
    /// settings, every configured BitTorrent server (with secrets redacted), and every
    /// tracked stream with its lifecycle status — the context needed to understand the log
    /// that follows.
    /// </summary>
    public static class StartupSummary
    {
        public static void Log(GlobalSpoolSettings settings, IReadOnlyList<TorrentStreamItem> streams)
        {
            LogGlobalSettings(settings);

            Logger.Log($"🖥️ Servers:");

            foreach (var kvp in settings.TorrentServers)
            {
                LogServerDetails(kvp.Key, kvp.Value);
            }

            Logger.Log($"📋 Streams:");
            LogStreams(streams);
        }

        public static void LogGlobalSettings(GlobalSpoolSettings settings)
        {
            var serverNames = string.Join(", ", settings.TorrentServers.Keys);
            string resolvedCacheDir = SettingsManager.GetCacheDirectory(settings.CacheDirectory);
            Logger.Log($"⚙️ Global settings: defaultServer='{settings.DefaultServerProfile}', servers=[{serverNames}], " +
                       $"spoolingTarget='{settings.DefaultSpoolingTarget}', libraryDir='{settings.LibraryDir}', " +
                       $"stagingHostPath='{settings.StagingHostPath}', libraryHostPath='{settings.LibraryHostPath}', " +
                       $"pollInterval={settings.PollIntervalSeconds}s, settling={settings.SettlingTimeSeconds}s, " +
                       $"cacheDir='{resolvedCacheDir}', serverRetryCount={settings.ServerRetryCount}, " +
                       $"safetyMargin={settings.SpoolingCapSafetyMarginPercent}%");
        }

        public static void LogServerDetails(string profileName, TorrentServerProfile p)
        {
            var mapping = p.ClientDownloadsMapping;
            string mappingStr = mapping == null ||
                (string.IsNullOrWhiteSpace(mapping.ClientVirtualPrefix) && string.IsNullOrWhiteSpace(mapping.AppVirtualPrefix))
                ? "(none)"
                : $"client='{mapping.ClientVirtualPrefix}' -> app='{mapping.AppVirtualPrefix}'";

            Logger.Log($"🖥️ Server '{profileName}': type={p.ClientType}, host={p.Host}, cap={p.SpoolingCapGb} GB, " +
                       $"mapping={mappingStr}, username={(string.IsNullOrEmpty(p.Username) ? "\"\"" : "[redacted]")}, " +
                       $"password={(string.IsNullOrEmpty(p.Password) ? "\"\"" : "[redacted]")}, " +
                       $"apiKey={(string.IsNullOrEmpty(p.ApiKey) ? "\"\"" : "[redacted]")}");
        }

        public static void LogStreams(IReadOnlyList<TorrentStreamItem> streams)
        {
            if (streams.Count == 0)
            {
                Logger.Log("📋 Streams: none.");
                return;
            }

            foreach (var s in streams.OrderBy(s => s.Id))
            {
                Logger.Log($"📋 Stream #{s.Id} '{s.Name}': status={s.Status}, server='{s.ServerProfileId}', " +
                           $"strategy={s.Strategy}, target='{s.SpoolingTargetOverride ?? "(default)"}', " +
                           $"moved={s.MovedCount}/{s.TotalCount}, priorityTerms='{s.PriorityTerms}', " +
                           $"dePriorityTerms='{s.DePriorityTerms}', created={s.CreatedUtc:yyyy-MM-dd HH:mm}");
            }
        }
    }
}
