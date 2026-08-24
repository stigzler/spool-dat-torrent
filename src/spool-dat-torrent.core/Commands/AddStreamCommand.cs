using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

        public AddStreamCommand(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        /// <summary>
        /// Add or update a stream. Returns the created/updated stream.
        /// When updating an existing stream, a null/empty <paramref name="serverProfileId"/>
        /// preserves the stream's existing server profile.
        /// </summary>
        public async Task<TorrentStreamItem> ExecuteAsync(
            string torrentIdentifier,
            string datFilePath,
            string? name,
            string? spoolingTargetOverride,
            string? serverProfileId,
            string? originalTorrentPath = null,
            string? originalMagnet = null,
            string? filter = null,
            SpoolingStrategy? strategy = null,
            CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SpoolDbContext>();
            await db.Database.EnsureCreatedAsync(cancellationToken);

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

                existing.Status = StreamLifecycleStatus.Active;
                if (!string.IsNullOrWhiteSpace(filter)) existing.FileFilter = filter;
                if (strategy.HasValue) existing.Strategy = strategy.Value;

                await db.SaveChangesAsync(cancellationToken);
                return existing;
            }

            var stream = new TorrentStreamItem
            {
                Id = await GetLowestFreeStreamIdAsync(db, cancellationToken),
                TorrentIdentifier = torrentIdentifier,
                Name = string.IsNullOrWhiteSpace(name) ? torrentIdentifier : name,
                DatFilePath = datFilePath,
                SpoolingTargetOverride = spoolingTargetOverride,
                ServerProfileId = serverProfileId ?? string.Empty,
                Status = StreamLifecycleStatus.Active,
                OriginalTorrentPath = originalTorrentPath,
                OriginalMagnet = originalMagnet,
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
