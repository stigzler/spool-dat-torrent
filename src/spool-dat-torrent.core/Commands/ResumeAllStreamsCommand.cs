using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SpoolDatTorrent.Core.Data;
using SpoolDatTorrent.Core.Helpers;
using SpoolDatTorrent.Core.Models;

namespace SpoolDatTorrent.Core.Commands
{
    /// <summary>
    /// Resumes (re-activates) all streams that were paused by the global "Pause All" action
    /// (PausedByGlobal == true), and clears the flag. Streams paused manually or completed
    /// are left untouched. Persisted in the DB so this survives an app restart.
    /// </summary>
    public class ResumeAllStreamsCommand
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public ResumeAllStreamsCommand(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        /// <summary>Resume all streams paused by the global pause. Returns the resumed IDs.</summary>
        public async Task<List<int>> ExecuteAsync(CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SpoolDbContext>();
            await db.Database.MigrateAsync(cancellationToken);

            var streams = await db.Streams
                .Where(s => s.PausedByGlobal)
                .ToListAsync(cancellationToken);

            var ids = new List<int>();
            foreach (var stream in streams)
            {
                stream.Status = StreamLifecycleStatus.Active;
                stream.PausedByGlobal = false;
                ids.Add(stream.Id);
            }

            await db.SaveChangesAsync(cancellationToken);
            Logger.Log($"▶️ Resumed all streams ({ids.Count} stream(s)).");
            return ids;
        }
    }
}
