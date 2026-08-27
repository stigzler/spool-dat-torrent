using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SpoolDatTorrent.Core.Configuration;
using SpoolDatTorrent.Core.Data;
using SpoolDatTorrent.Core.Helpers;
using SpoolDatTorrent.Core.Interfaces;

namespace SpoolDatTorrent.Core.Commands
{
    /// <summary>
    /// Cancels all streams: removes every torrent (and its scratch files) from every
    /// configured BitTorrent client and deletes all stream rows from the database. Files
    /// already moved to the destination are kept. Reusable by the CLI, Docker web UI, and
    /// desktop apps.
    /// </summary>
    public class CancelAllStreamsCommand
    {
        private readonly IBitTorrentClientFactory _clientFactory;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly GlobalSpoolSettings _settings;
        private readonly StreamFileCache _fileCache;

        public CancelAllStreamsCommand(
            IBitTorrentClientFactory clientFactory,
            IServiceScopeFactory scopeFactory,
            IOptions<GlobalSpoolSettings> settings)
        {
            _clientFactory = clientFactory;
            _scopeFactory = scopeFactory;
            _settings = settings.Value;
            _fileCache = new StreamFileCache(settings);
        }

        /// <summary>
        /// Cancel every stream. Removes all torrents from every configured client and clears the DB.
        /// </summary>
        /// <returns>The number of torrents removed across all clients.</returns>
        public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
        {
            int removed = 0;

            // 1. Remove every torrent from every configured server profile. A server that
            //    is unreachable/failing must not stop us from cancelling the others, so we
            //    catch and continue per server.
            foreach (var profileName in _settings.TorrentServers.Keys)
            {
                try
                {
                    var client = _clientFactory.GetClient(profileName);
                    await client.AuthenticateAsync(cancellationToken);

                    var hashes = await client.GetAllTorrentHashesAsync(cancellationToken);
                    foreach (var hash in hashes)
                    {
                        await client.DeleteTorrentAsync(hash, deleteFiles: true, cancellationToken);
                        removed++;
                    }
                }
                catch (Exception ex)
                {
                    // Log and continue so other servers are still cancelled.
                    Logger.LogError($"Failed to cancel torrents on server '{profileName}': {ex.Message}");
                }
            }

            // 2. Clear all stream rows from the database, and delete each stream's cached files.
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SpoolDbContext>();
            var allStreams = await db.Streams.ToListAsync(cancellationToken);

            foreach (var stream in allStreams)
            {
                _fileCache.DeleteCachedFiles(stream.CachedTorrentPath, stream.CachedDatPath);
            }

            db.Streams.RemoveRange(allStreams);
            await db.SaveChangesAsync(cancellationToken);

            Logger.Log($"🗑️ Cancelled all streams ({removed} torrent(s) removed).");
            return removed;
        }
    }
}
