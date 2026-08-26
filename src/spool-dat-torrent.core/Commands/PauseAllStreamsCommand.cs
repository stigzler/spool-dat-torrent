using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SpoolDatTorrent.Core.Data;
using SpoolDatTorrent.Core.Models;

namespace SpoolDatTorrent.Core.Commands
{
    /// <summary>
    /// Pauses every Active stream, returning the IDs that were Active so a global "Resume
    /// All" can restore exactly those streams (and not ones already Paused/Completed).
    /// Reusable by the CLI, Docker web UI, and desktop apps.
    /// </summary>
    public class PauseAllStreamsCommand
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public PauseAllStreamsCommand(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        /// <summary>Pause all Active streams. Returns the IDs that were Active.</summary>
        public async Task<List<int>> ExecuteAsync(CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SpoolDbContext>();
            await db.Database.MigrateAsync(cancellationToken);

            var activeStreams = await db.Streams
                .Where(s => s.Status == StreamLifecycleStatus.Active)
                .ToListAsync(cancellationToken);

            var ids = activeStreams.Select(s => s.Id).ToList();

            foreach (var stream in activeStreams)
            {
                stream.Status = StreamLifecycleStatus.Paused;
                stream.PausedByGlobal = true;
            }

            await db.SaveChangesAsync(cancellationToken);
            return ids;
        }
    }
}
