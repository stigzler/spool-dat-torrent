using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SpoolDatTorrent.Core.Data;
using SpoolDatTorrent.Core.Models;

namespace SpoolDatTorrent.Core.Commands
{
    /// <summary>
    /// Manually retries a stream by setting its status back to Active. This is the manual
    /// retry facility needed by the permanent web UI / desktop service (where the engine
    /// keeps running and errored streams would otherwise stay stopped). Reusable by the
    /// CLI, Docker web UI, and desktop apps.
    /// </summary>
    public class RetryStreamCommand
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public RetryStreamCommand(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        /// <summary>
        /// Set a single stream back to Active by its torrent identifier.
        /// </summary>
        /// <returns>True if a stream was found and re-activated; false otherwise.</returns>
        public async Task<bool> ExecuteAsync(string torrentIdentifier, CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SpoolDbContext>();
            var stream = await db.Streams.FirstOrDefaultAsync(s => s.TorrentIdentifier == torrentIdentifier, cancellationToken);

            if (stream == null)
            {
                return false;
            }

            stream.Status = StreamLifecycleStatus.Active;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
    }
}
