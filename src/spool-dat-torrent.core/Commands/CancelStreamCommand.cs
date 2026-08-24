using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SpoolDatTorrent.Core.Configuration;
using SpoolDatTorrent.Core.Data;
using SpoolDatTorrent.Core.Helpers;
using SpoolDatTorrent.Core.Interfaces;
using SpoolDatTorrent.Core.Models;

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

            return await ExecuteInternalAsync(db, stream, cancellationToken);
        }

        /// <summary>
        /// Cancel the stream identified by its numeric database Id.
        /// </summary>
        /// <returns>True if a stream row was found and deleted; false otherwise.</returns>
        public async Task<bool> ExecuteByIdAsync(int streamId, CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SpoolDbContext>();
            var stream = await db.Streams.FirstOrDefaultAsync(s => s.Id == streamId, cancellationToken);

            if (stream == null)
            {
                return false;
            }

            return await ExecuteInternalAsync(db, stream, cancellationToken);
        }

        private async Task<bool> ExecuteInternalAsync(SpoolDbContext db, TorrentStreamItem stream, CancellationToken cancellationToken)
        {
            string profileName = string.IsNullOrWhiteSpace(stream.ServerProfileId)
                ? _settings.DefaultServerProfile
                : stream.ServerProfileId;

            // 2. Remove the torrent (and its scratch files) from the client. If the client
            //    is unreachable/failing, log it but still cancel the stream locally so it is
            //    no longer tracked.
            try
            {
                var client = _clientFactory.GetClient(profileName);
                await client.AuthenticateAsync(cancellationToken);
                await client.DeleteTorrentAsync(stream.TorrentIdentifier, deleteFiles: true, cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.Log($"[Error] Failed to remove torrent from server '{profileName}': {ex.Message}");
            }

            // 3. Delete the stream row from the database.
            db.Streams.Remove(stream);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
    }
}
