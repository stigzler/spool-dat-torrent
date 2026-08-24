using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SpoolDatTorrent.Core.Data;
using SpoolDatTorrent.Core.DTOs;

namespace SpoolDatTorrent.Core.Commands
{
    /// <summary>
    /// Returns details of all streams currently tracked in the database. Reusable by the
    /// CLI, Docker web UI, and desktop apps.
    /// </summary>
    public class ListStreamsCommand
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public ListStreamsCommand(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        /// <summary>
        /// List all streams, optionally filtered to a specific lifecycle status.
        /// </summary>
        public async Task<IReadOnlyList<StreamDetails>> ExecuteAsync(
            string? statusFilter = null,
            CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SpoolDbContext>();
            await db.Database.EnsureCreatedAsync(cancellationToken);

            var query = db.Streams.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(statusFilter))
            {
                query = query.Where(s => s.Status.ToString() == statusFilter);
            }

            var streams = await query.OrderBy(s => s.CreatedUtc).ToListAsync(cancellationToken);

            return streams
                .Select(s => new StreamDetails
                {
                    Id = s.Id,
                    Name = s.Name,
                    TorrentIdentifier = s.TorrentIdentifier,
                    DatFilePath = s.DatFilePath,
                    SpoolingTargetOverride = s.SpoolingTargetOverride,
                    ServerProfileId = s.ServerProfileId,
                    Status = s.Status.ToString(),
                    CreatedUtc = s.CreatedUtc,
                    MovedCount = s.MovedCount,
                    TotalCount = s.TotalCount
                })
                .ToList();
        }
    }
}
