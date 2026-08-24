using System.Collections.Generic;

namespace SpoolDatTorrent.Core.DTOs
{
    /// <summary>
    /// A snapshot of a single spooling job's progress, used by host applications
    /// (CLI, Docker, desktop) to render a live status display.
    /// </summary>
    public class StreamProgressInfo
    {
        public string Name { get; set; } = string.Empty;
        public string TorrentIdentifier { get; set; } = string.Empty;

        /// <summary>Lifecycle status of the job (Active, Paused, Completed, Error).</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>Number of desired (DAT-matched) files already moved to the destination.</summary>
        public int MovedCount { get; set; }

        /// <summary>Total number of desired (DAT-matched) files in the torrent.</summary>
        public int TotalCount { get; set; }

        /// <summary>Progress of the whole job as a fraction (0.0 - 1.0).</summary>
        public double Progress => TotalCount == 0 ? 0 : (double)MovedCount / TotalCount;

        /// <summary>Files in the current batch and their download progress.</summary>
        public List<FileProgressInfo> Files { get; set; } = new();
    }
}
