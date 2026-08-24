using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SpoolDatTorrent.Core.Configuration;
using SpoolDatTorrent.Core.Data;
using SpoolDatTorrent.Core.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SpoolDatTorrent.Core.Commands
{
    /// <summary>
    /// Deletes a BitTorrent server profile, guarding against deletion that would break
    /// existing streams or leave the app without a default profile. Reusable by the CLI,
    /// Docker web UI, and desktop apps.
    /// </summary>
    public class DeleteServerProfileCommand
    {
        private readonly GlobalSpoolSettings _settings;
        private readonly IServiceScopeFactory _scopeFactory;

        public DeleteServerProfileCommand(IOptions<GlobalSpoolSettings> settings, IServiceScopeFactory scopeFactory)
        {
            _settings = settings.Value;
            _scopeFactory = scopeFactory;
        }

        /// <summary>
        /// Attempt to delete the named server profile.
        /// </summary>
        /// <returns>A result describing success or the reason for refusal.</returns>
        public async Task<DeleteServerProfileResult> ExecuteAsync(string profileName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(profileName))
            {
                return new DeleteServerProfileResult
                {
                    Success = false,
                    Message = "No server profile name was provided."
                };
            }

            if (!_settings.TorrentServers.ContainsKey(profileName))
            {
                return new DeleteServerProfileResult
                {
                    Success = false,
                    Message = $"Server profile '{profileName}' does not exist."
                };
            }

            // Refuse to delete the profile that is the configured default, since the app
            // needs a default to resolve streams that don't specify an explicit profile.
            var isDefault = string.Equals(_settings.DefaultServerProfile, profileName, StringComparison.OrdinalIgnoreCase);
            if (isDefault)
            {
                return new DeleteServerProfileResult
                {
                    Success = false,
                    Message = $"Cannot delete '{profileName}' because it is the default server profile. Choose a different default first."
                };
            }

            // Refuse if any stream references the profile. This includes streams that
            // explicitly reference it AND (when it's the default) streams with no explicit
            // profile that would resolve to it. Deleting would leave those streams broken.
            var referencing = await FindReferencingStreamsAsync(profileName, cancellationToken);

            if (referencing.Count > 0)
            {
                return new DeleteServerProfileResult
                {
                    Success = false,
                    Message = $"Cannot delete server profile '{profileName}' because it is assigned to the following stream(s): {string.Join(", ", referencing)}. Cancel or reassign those streams first.",
                    ReferencingStreams = referencing
                };
            }

            _settings.TorrentServers.Remove(profileName);

            // If the deleted profile happened to be the default (defensive; normally refused
            // above), point the default at another profile.
            if (string.Equals(_settings.DefaultServerProfile, profileName, StringComparison.OrdinalIgnoreCase))
            {
                _settings.DefaultServerProfile = _settings.TorrentServers.Keys.FirstOrDefault() ?? string.Empty;
            }

            SettingsManager.SaveSettings(_settings);

            return new DeleteServerProfileResult
            {
                Success = true,
                Message = $"Deleted server profile '{profileName}'."
            };
        }

        private async Task<IReadOnlyList<string>> FindReferencingStreamsAsync(string profileName, CancellationToken cancellationToken)
        {
            var isDefault = string.Equals(_settings.DefaultServerProfile, profileName, StringComparison.OrdinalIgnoreCase);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SpoolDbContext>();

            // Ensure the schema exists. The DB is created lazily on first stream use, so a
            // fresh install may not have the Streams table yet — querying it directly would
            // throw "no such table: Streams".
            await db.Database.EnsureCreatedAsync(cancellationToken);

            var names = await db.Streams
                .Where(s => s.ServerProfileId == profileName ||
                            (isDefault && (s.ServerProfileId == null || s.ServerProfileId == string.Empty)))
                .Select(s => s.Name)
                .ToListAsync(cancellationToken);

            return names;
        }
    }
}
