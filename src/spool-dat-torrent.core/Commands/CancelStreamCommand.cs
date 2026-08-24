using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SpoolDatTorrent.Core.Configuration;
using SpoolDatTorrent.Core.Data;
using SpoolDatTorrent.Core.Interfaces;

namespace SpoolDatTorrent.Core.Commands
{
    /// <summary>
    /// Cancels a single stream: removes its torrent (and scratch files) from the BitTorrent
    /// client and deletes the stream row from the database. Files already moved to the
    /// destination are kept. Reusable by the CLI, Docker web UI, and desktop apps.
    /// </summary>
    public class CancelStreamCommand
    {
        private readonly IBitTorrentClientFactory _clientFactory;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly GlobalSpoolSettings _settings;

        public CancelStreamCommand(
            IBitTorrentClientFactory clientFactory,
            IServiceScopeFactory scopeFactory,
            IOptions<GlobalSpoolSettings> settings)
        {
            _clientFactory = clientFactory;
            _scopeFactory = scopeFactory;
            _settings = settings.Value;
        }

        /// <summary>
        /// Cancel the stream identified by the given info-hash.
        /// </summary>
        /// <returns>True if a stream row was found and deleted; false otherwise.</returns>
        public async Task<bool> ExecuteAsync(string torrentIdentifier, CancellationToken cancellationToken = default)
        {
            // 1. Look up the stream to determine which server profile it belongs to.
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SpoolDbContext>();
            var stream = await db.Streams.FirstOrDefaultAsync(s => s.TorrentIdentifier == torrentIdentifier, cancellationToken);

            if (stream == null)
            {
                return false;
            }

            string profileName = string.IsNullOrWhiteSpace(stream.ServerProfileId)
                ? _settings.DefaultServerProfile
                : stream.ServerProfileId;

            // 2. Remove the torrent (and its scratch files) from the client.
            var client = _clientFactory.GetClient(profileName);
            await client.AuthenticateAsync(cancellationToken);
            await client.DeleteTorrentAsync(torrentIdentifier, deleteFiles: true, cancellationToken);

            // 3. Delete the stream row from the database.
            db.Streams.Remove(stream);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
    }
}
