using Microsoft.Extensions.Options;
using SpoolDatTorrent.Core.Configuration;
using SpoolDatTorrent.Core.DTOs;
using SpoolDatTorrent.Core.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SpoolDatTorrent.Core.Commands
{
    /// <summary>
    /// Creates a new BitTorrent server profile with sensible defaults, mutating the live
    /// settings singleton and persisting it. Reusable by the CLI, Docker web UI, and
    /// desktop apps.
    /// </summary>
    public class AddServerProfileCommand
    {
        private readonly GlobalSpoolSettings _settings;

        public AddServerProfileCommand(IOptions<GlobalSpoolSettings> settings)
        {
            _settings = settings.Value;
        }

        /// <summary>
        /// Add a new server profile to the live settings and persist it.
        /// </summary>
        public Task<AddServerProfileResult> ExecuteAsync(CancellationToken cancellationToken = default)
        {
            var profileName = GenerateUniqueName();

            _settings.TorrentServers[profileName] = new TorrentServerProfile
            {
                ClientType = BitTorrentClientType.QBittorrent,
                Host = "http://localhost:8080",
                Username = "admin",
                Password = string.Empty,
                ApiKey = string.Empty,
                SpoolingCapGb = 500,
                ClientDownloadsMapping = new ClientDownloadsMapping()
            };

            SettingsManager.SaveSettings(_settings);

            Logger.Log($"➕ Created server profile '{profileName}'.");
            StartupSummary.LogServerDetails(profileName, _settings.TorrentServers[profileName]);
            return Task.FromResult(new AddServerProfileResult
            {
                Success = true,
                Message = $"Created server profile '{profileName}'.",
                ProfileName = profileName,
                Profile = _settings.TorrentServers[profileName]
            });
        }

        private static string GenerateUniqueName()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // avoid 0/O, 1/I confusion
            var random = new Random();
            var suffix = new string(Enumerable.Repeat(chars, 4).Select(s => s[random.Next(s.Length)]).ToArray());
            return $"New Server {suffix}";
        }
    }
}
