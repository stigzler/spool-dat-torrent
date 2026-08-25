using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SpoolDatTorrent.Core.Commands;

namespace SpoolDatTorrent.Web
{
    /// <summary>
    /// Holds global "Pause All" / "Resume All" state. When the user pauses everything, the
    /// set of stream IDs that were Active is snapshotted so that resuming only restarts the
    /// streams that were running — not ones that were already Paused/Completed.
    /// </summary>
    public class GlobalPauseService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly object _lock = new();

        private HashSet<int> _snapshot = new();

        public bool IsPaused { get; private set; }

        public GlobalPauseService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        /// <summary>Pause every Active stream and remember which ones were running.</summary>
        public async Task PauseAllAsync()
        {
            List<int> activeIds;
            using (var scope = _scopeFactory.CreateScope())
            {
                var cmd = scope.ServiceProvider.GetRequiredService<PauseAllStreamsCommand>();
                activeIds = await cmd.ExecuteAsync();
            }

            lock (_lock)
            {
                _snapshot = new HashSet<int>(activeIds);
                IsPaused = true;
            }
        }

        /// <summary>Resume only the streams that were Active when the global pause happened.</summary>
        public async Task ResumeAllAsync()
        {
            List<int> toResume;
            lock (_lock)
            {
                toResume = new List<int>(_snapshot);
                _snapshot.Clear();
                IsPaused = false;
            }

            using var scope = _scopeFactory.CreateScope();
            var cmd = scope.ServiceProvider.GetRequiredService<ResumeAllStreamsCommand>();
            await cmd.ExecuteAsync(toResume);
        }
    }
}
