using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SpoolDatTorrent.Core.Data;
using SpoolDatTorrent.Core.Helpers;
using SpoolDatTorrent.Core.Models;

namespace SpoolDatTorrent.Core.Commands
{
    /// <summary>
    /// Sets a stream's lifecycle status (e.g. Paused or Active). Reusable by the CLI,
    /// Docker web UI, and desktop apps. The engine only spools streams whose status is
    /// Active, so setting Paused stops spooling without deleting the stream.
    /// </summary>
    public class SetStreamStatusCommand
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public SetStreamStatusCommand(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        /// <summary>
        /// Set a stream's status by its torrent identifier.
        /// </summary>
        /// <returns>True if a stream was found and updated; false otherwise.</returns>
        public async Task<bool> ExecuteAsync(string torrentIdentifier, StreamLifecycleStatus status, CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SpoolDbContext>();
            var stream = await db.Streams.FirstOrDefaultAsync(s => s.TorrentIdentifier == torrentIdentifier, cancellationToken);

            if (stream == null)
            {
                return false;
            }

            return await SetStatusAsync(db, stream, status, cancellationToken);
        }

        /// <summary>
        /// Set a stream's status by its numeric database Id.
        /// </summary>
        /// <returns>True if a stream was found and updated; false otherwise.</returns>
        public async Task<bool> ExecuteByIdAsync(int streamId, StreamLifecycleStatus status, CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SpoolDbContext>();
            var stream = await db.Streams.FirstOrDefaultAsync(s => s.Id == streamId, cancellationToken);

            if (stream == null)
            {
                return false;
            }

            return await SetStatusAsync(db, stream, status, cancellationToken);
        }

        private async Task<bool> SetStatusAsync(SpoolDbContext db, TorrentStreamItem stream, StreamLifecycleStatus status, CancellationToken cancellationToken)
        {
            stream.Status = status;
            await db.SaveChangesAsync(cancellationToken);
            Logger.Log($"🔀 Set stream '{stream.Name}' status to {status}.");
            return true;
        }
    }
}
