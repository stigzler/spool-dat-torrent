using System.Collections.Generic;
using SpoolDatTorrent.Core.DTOs;

namespace SpoolDatTorrent.Core.Interfaces
{
    /// <summary>
    /// Host-agnostic reporting seam for spooling progress. The engine emits structured
    /// events through this interface so host applications (CLI, Docker service, desktop
    /// app) can render a live status display. Implementations are optional: if none is
    /// registered, the engine simply skips reporting and continues to use file logging.
    /// </summary>
    public interface ISpoolingProgressReporter
    {
        /// <summary>Report a snapshot of all known jobs (streams) and their progress.</summary>
        void ReportStreams(IReadOnlyList<StreamProgressInfo> streams);

        /// <summary>Report a single transient status message (e.g. "Halting torrent to copy 8 files...").</summary>
        void ReportStatus(string message);
    }
}
