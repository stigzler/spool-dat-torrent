using SpoolDatTorrent.Core.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace SpoolDatTorrent.Core.Models
{
    public class TorrentStreamItem
    {
        public int Id { get; set; }
        public string TorrentIdentifier { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string DatFilePath { get; set; } = string.Empty;
        public string? OriginalTorrentPath { get; set; }
        public string? OriginalMagnet { get; set; }

        /// <summary>Original path of the DAT file supplied when the stream was added. Kept so
        /// the UI can display the real DAT filename rather than the GUID cache/temp name.</summary>
        public string? OriginalDatPath { get; set; }

        /// <summary>Cached copy of the .torrent file, set when the stream is added. Null if not cached yet.</summary>
        public string? CachedTorrentPath { get; set; }

        /// <summary>Cached copy of the .dat file, set when the stream is added. Null if not cached yet.</summary>
        public string? CachedDatPath { get; set; }
        public string? SpoolingTargetOverride { get; set; }
        public SpoolingStrategy Strategy { get; set; } = SpoolingStrategy.MoveFiles;
        public string FileFilter { get; set; } = "*.*";

        /// <summary>
        /// Per-stream override for the settling time (seconds) — how long to wait after
        /// pausing a torrent before moving its completed files. Null falls back to the
        /// global default (<see cref="GlobalSpoolSettings.SettlingTimeSeconds"/>).
        /// </summary>
        public int? SettlingTimeSeconds { get; set; }

        /// <summary>
        /// Comma-separated filename substrings to download FIRST (e.g. "(USA),(Europe),(World)").
        /// Empty = no prioritisation.
        /// </summary>
        public string PriorityTerms { get; set; } = string.Empty;

        /// <summary>
        /// Comma-separated filename substrings to download LAST (e.g. "(Japan),(China)").
        /// Empty = no de-prioritisation.
        /// </summary>
        public string DePriorityTerms { get; set; } = string.Empty;

        /// <summary>
        /// Per-stream override for the maximum spool size (GB). Null means the stream uses
        /// the fair-share split of its server's <see cref="TorrentServerProfile.SpoolingCapGb"/>.
        /// When set, it must not exceed the server profile's cap.
        /// </summary>
        public long? SpoolingCapGb { get; set; }

        public StreamLifecycleStatus Status { get; set; } = StreamLifecycleStatus.Active;

        /// <summary>
        /// True when this stream was paused by the global "Pause All" action (as opposed to a
        /// manual per-stream pause). Persisted so the global Resume can restore exactly the
        /// streams it paused, even after an app restart.
        /// </summary>
        public bool PausedByGlobal { get; set; }

        /// <summary>
        /// True when a rate limit has actually been applied to this stream's torrent in the
        /// BitTorrent client. Set by the engine when it throttles a batch, and cleared when
        /// the strategy is changed away from <see cref="SpoolingStrategy.RateLimit"/>. Persisted
        /// so the "Rate Limited" indicator survives an app restart while the limit is active.
        /// </summary>
        public bool IsRateLimited { get; set; }

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public List<TorrentFileItem> Files { get; set; } = new();
        public string ServerProfileId { get; set; } = string.Empty;

        /// <summary>Number of desired (DAT-matched) files already moved to the destination.</summary>
        public int MovedCount { get; set; }

        /// <summary>Total number of desired (DAT-matched) files in the torrent.</summary>
        public int TotalCount { get; set; }
    }
}
