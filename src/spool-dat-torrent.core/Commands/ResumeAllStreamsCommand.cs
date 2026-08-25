using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SpoolDatTorrent.Core.Data;
using SpoolDatTorrent.Core.Models;

namespace SpoolDatTorrent.Core.Commands
{
    /// <summary>
    /// Resumes (re-activates) a specific set of stream IDs — those that were Active when a
    /// global "Pause All" was triggered. Streams that were already Paused/Completed are
    /// deliberately not included. Reusable by the CLI, Docker web UI, and desktop apps.
    /// </summary>
    public class ResumeAllStreamsCommand
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public ResumeAllStreamsCommand(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        /// <summary>Set the given stream IDs back to Active.</summary>
        public async Task ExecuteAsync(IEnumerable<int> streamIds, CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SpoolDbContext>();
            await db.Database.MigrateAsync(cancellationToken);

            var ids = new HashSet<int>(streamIds);
            if (ids.Count == 0)
            {
                return;
            }

            var streams = await db.Streams
                .Where(s => ids.Contains(s.Id))
                .ToListAsync(cancellationToken);

            foreach (var stream in streams)
            {
                stream.Status = StreamLifecycleStatus.Active;
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
