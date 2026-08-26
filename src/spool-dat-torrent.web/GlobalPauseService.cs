using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SpoolDatTorrent.Core.Commands;
using SpoolDatTorrent.Core.Progress;

namespace SpoolDatTorrent.Web
{
    /// <summary>
    /// Global "Pause All" / "Resume All" orchestration. State is persisted via the
    /// <c>PausedByGlobal</c> flag on each stream, so it survives app/circuit restarts.
    /// </summary>
    public class GlobalPauseService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly InMemoryProgressStore _store;

        /// <summary>
        /// True when a global pause is active (any stream is marked PausedByGlobal). Derived
        /// from the DB on first access so it is correct immediately after a restart.
        /// </summary>
        public bool IsPaused { get; private set; }

        public GlobalPauseService(IServiceScopeFactory scopeFactory, InMemoryProgressStore store)
        {
            _scopeFactory = scopeFactory;
            _store = store;
        }

        /// <summary>Load whether a global pause is currently active from the DB.</summary>
        public async Task InitializeAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SpoolDatTorrent.Core.Data.SpoolDbContext>();
            IsPaused = await db.Streams.AnyAsync(s => s.PausedByGlobal);
        }

        /// <summary>Pause every Active stream, marking them PausedByGlobal.</summary>
        public async Task PauseAllAsync()
        {
            List<int> activeIds;
            using (var scope = _scopeFactory.CreateScope())
            {
                var cmd = scope.ServiceProvider.GetRequiredService<PauseAllStreamsCommand>();
                activeIds = await cmd.ExecuteAsync();
            }

            IsPaused = true;
            _store.UpdateStatuses(activeIds, "Paused");
        }

        /// <summary>Resume all streams paused by the global pause.</summary>
        public async Task ResumeAllAsync()
        {
            List<int> resumedIds;
            using (var scope = _scopeFactory.CreateScope())
            {
                var cmd = scope.ServiceProvider.GetRequiredService<ResumeAllStreamsCommand>();
                resumedIds = await cmd.ExecuteAsync();
            }

            IsPaused = false;
            _store.UpdateStatuses(resumedIds, "Active");
        }
    }
}
