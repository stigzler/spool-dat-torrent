using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SpoolDatTorrent.Core.Configuration;
using SpoolDatTorrent.Core.Data;
using SpoolDatTorrent.Core.Models;

namespace SpoolDatTorrent.Core.Commands
{
    /// <summary>
    /// Adds or updates a spooling stream (torrent + DAT) in the database. Reusable by the
    /// CLI, Docker web UI, and desktop apps. Stream IDs reuse the lowest free value so
    /// deleted IDs are reclaimed rather than always incrementing.
    /// </summary>
    public class AddStreamCommand
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly GlobalSpoolSettings _settings;
        private readonly StreamFileCache _fileCache;

        public AddStreamCommand(
            IServiceScopeFactory scopeFactory,
            IOptions<GlobalSpoolSettings> settings)
        {
            _scopeFactory = scopeFactory;
            _settings = settings.Value;
            _fileCache = new StreamFileCache(settings);
        }

        /// <summary>
        /// Add or update a stream. Returns the created/updated stream.
        /// When updating an existing stream, a null/empty <paramref name="serverProfileId"/>
        /// preserves the stream's existing server profile. When creating a new stream, a
        /// null/empty <paramref name="serverProfileId"/> resolves to the configured default.
        /// </summary>
        public async Task<TorrentStreamItem> ExecuteAsync(
            string torrentIdentifier,
            string datFilePath,
            string? name,
            string? spoolingTargetOverride,
            string? serverProfileId,
            string? originalTorrentPath = null,
            string? originalMagnet = null,
            string? originalDatPath = null,
            string? filter = null,
            SpoolingStrategy? strategy = null,
            CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SpoolDbContext>();
            await db.Database.MigrateAsync(cancellationToken);

            var existing = await db.Streams.FirstOrDefaultAsync(s => s.TorrentIdentifier == torrentIdentifier, cancellationToken);

            if (existing != null)
            {
                if (!string.IsNullOrWhiteSpace(name)) existing.Name = name;
                existing.DatFilePath = datFilePath;
                existing.SpoolingTargetOverride = spoolingTargetOverride;

                // Preserve the existing server profile unless an explicit one is supplied.
                if (!string.IsNullOrWhiteSpace(serverProfileId))
                {
                    existing.ServerProfileId = serverProfileId;
                }

                // Re-cache the source files (overwriting any prior cached copies) so updated
                // sources take effect. Magnet-only streams keep their cached torrent if any.
                var (cachedTorrent, cachedDat) = _fileCache.CacheFiles(torrentIdentifier, originalTorrentPath ?? existing.OriginalTorrentPath, datFilePath);
                if (cachedTorrent != null) existing.CachedTorrentPath = cachedTorrent;
                if (cachedDat != null) existing.CachedDatPath = cachedDat;
                if (!string.IsNullOrWhiteSpace(originalTorrentPath)) existing.OriginalTorrentPath = originalTorrentPath;
                if (!string.IsNullOrWhiteSpace(originalMagnet)) existing.OriginalMagnet = originalMagnet;
                if (!string.IsNullOrWhiteSpace(originalDatPath)) existing.OriginalDatPath = originalDatPath;

                existing.Status = StreamLifecycleStatus.Active;
                if (!string.IsNullOrWhiteSpace(filter)) existing.FileFilter = filter;
                if (strategy.HasValue) existing.Strategy = strategy.Value;

                await db.SaveChangesAsync(cancellationToken);
                return existing;
            }

            // For a new stream, resolve the server profile: explicit value, else the
            // configured default (rather than persisting an empty string).
            string resolvedServer = !string.IsNullOrWhiteSpace(serverProfileId)
                ? serverProfileId
                : _settings.DefaultServerProfile;

            // Copy the source files into the cache so the stream is independent of the
            // original paths (which the user may delete later).
            var cacheResult = _fileCache.CacheFiles(torrentIdentifier, originalTorrentPath, datFilePath);

            var stream = new TorrentStreamItem
            {
                Id = await GetLowestFreeStreamIdAsync(db, cancellationToken),
                TorrentIdentifier = torrentIdentifier,
                Name = string.IsNullOrWhiteSpace(name) ? torrentIdentifier : name,
                DatFilePath = datFilePath,
                SpoolingTargetOverride = spoolingTargetOverride,
                ServerProfileId = resolvedServer,
                Status = StreamLifecycleStatus.Active,
                OriginalTorrentPath = originalTorrentPath,
                OriginalMagnet = originalMagnet,
                OriginalDatPath = originalDatPath,
                CachedTorrentPath = cacheResult.CachedTorrentPath,
                CachedDatPath = cacheResult.CachedDatPath,
                FileFilter = string.IsNullOrWhiteSpace(filter) ? "*.*" : filter,
                Strategy = strategy ?? SpoolingStrategy.MoveFiles
            };

            db.Streams.Add(stream);
            await db.SaveChangesAsync(cancellationToken);
            return stream;
        }

        private static async Task<int> GetLowestFreeStreamIdAsync(SpoolDbContext db, CancellationToken cancellationToken)
        {
            var usedIds = await db.Streams.Select(s => s.Id).ToListAsync(cancellationToken);
            var used = new HashSet<int>(usedIds);

            for (int candidate = 1; candidate <= used.Count + 1; candidate++)
            {
                if (!used.Contains(candidate))
                {
                    return candidate;
                }
            }

            return used.Count + 1;
        }
    }
}
