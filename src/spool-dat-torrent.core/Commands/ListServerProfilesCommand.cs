using System.Linq;
using Microsoft.Extensions.Options;
using SpoolDatTorrent.Core.Configuration;
using SpoolDatTorrent.Core.DTOs;

namespace SpoolDatTorrent.Core.Commands
{
    /// <summary>
    /// Returns details of all configured BitTorrent server profiles. Reusable by the CLI,
    /// Docker web UI, and desktop apps.
    /// </summary>
    public class ListServerProfilesCommand
    {
        private readonly GlobalSpoolSettings _settings;

        public ListServerProfilesCommand(IOptions<GlobalSpoolSettings> settings)
        {
            _settings = settings.Value;
        }

        public Task<IReadOnlyList<ServerProfileDetails>> ExecuteAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ServerProfileDetails> profiles = _settings.TorrentServers
                .Select(kv => new ServerProfileDetails
                {
                    Name = kv.Key,
                    ClientType = kv.Value.ClientType,
                    Host = kv.Value.Host,
                    Username = kv.Value.Username,
                    HasApiKey = !string.IsNullOrWhiteSpace(kv.Value.ApiKey),
                    SpoolingCapGb = kv.Value.SpoolingCapGb
                })
                .ToList();

            return Task.FromResult(profiles);
        }
    }
}
