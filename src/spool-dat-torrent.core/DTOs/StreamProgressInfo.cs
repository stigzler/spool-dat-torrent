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

        /// <summary>The database Id of the stream.</summary>
        public int StreamId { get; set; }

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

        /// <summary>Name of the BitTorrent server profile this stream runs on.</summary>
        public string ServerName { get; set; } = string.Empty;

        /// <summary>UTC timestamp when the stream was created.</summary>
        public System.DateTime CreatedUtc { get; set; }

        /// <summary>Total size in bytes of all desired (DAT-matched) files in the torrent.</summary>
        public long TotalSizeBytes { get; set; }

        /// <summary>
        /// The dynamic batch-size cap (in bytes) this stream is currently allocated on its
        /// server, after the fair-share split across active streams and the safety margin.
        /// </summary>
        public long AllocatedCapBytes { get; set; }

        /// <summary>The latest human-readable status message emitted for this stream (e.g.
        /// "Halting torrent to move 8 completed files...").</summary>
        public string StatusMessage { get; set; } = string.Empty;

        /// <summary>Total size in bytes of the torrent as reported by the BitTorrent client.</summary>
        public long ClientSizeBytes { get; set; }

        /// <summary>Bytes downloaded so far as reported by the BitTorrent client.</summary>
        public long ClientDownloadedBytes { get; set; }

        /// <summary>The torrent's state as reported by the client (e.g. "downloading",
        /// "pausedUP", "checking", "stalledUP").</summary>
        public string ClientState { get; set; } = string.Empty;

        /// <summary>Number of connected seeds as reported by the client.</summary>
        public int ClientSeeds { get; set; }

        /// <summary>Total number of seeds in the swarm as reported by the client.</summary>
        public int ClientSeedsTotal { get; set; }

        /// <summary>Number of connected peers as reported by the client.</summary>
        public int ClientPeers { get; set; }

        /// <summary>Total number of leechers in the swarm as reported by the client.</summary>
        public int ClientPeersTotal { get; set; }

        /// <summary>Download speed in bytes/second as reported by the client.</summary>
        public long ClientDownSpeed { get; set; }

        /// <summary>ETA in seconds as reported by the client (-1 if unknown).</summary>
        public long ClientEta { get; set; }
    }
}
