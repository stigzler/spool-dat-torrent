using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace SpoolDatTorrent.Core.Configuration
{
    public class GlobalSpoolSettings
    {
        public string DefaultServerProfile { get; set; } = "LocalQBit";
        public Dictionary<string, TorrentServerProfile> TorrentServers { get; set; } = new();

        /// <summary>
        /// A real folder path on the machine running SpoolDatTorrent where completed files
        /// are moved to by default. Files go into a subfolder named after the torrent.
        /// A stream's per-stream destination override takes precedence over this.
        /// Examples: "C:\Spooled Output" (Windows) or "/mnt/pool/Media/Games/roms/unsorted" (Linux).
        /// </summary>
        public string DefaultSpoolingTarget { get; set; } = string.Empty;

        /// <summary>
        /// Password required to log in to the web UI. Single-admin, LAN-only use; compared
        /// as plaintext on login. Empty means the web UI is not protected.
        /// </summary>
        public string AdminPassword { get; set; } = string.Empty;

        /// <summary>How often the engine polls the BitTorrent client, in seconds.</summary>
        [Range(1, 60)]
        public int PollIntervalSeconds { get; set; } = 2;

        /// <summary>Time to wait after pausing before moving files, in seconds.</summary>
        [Range(1, 480)]
        public int SettlingTimeSeconds { get; set; } = 30;

        /// <summary>
        /// Directory where per-stream copies of the .torrent and .dat files are cached,
        /// so streams remain usable if the user later deletes the original files. If empty,
        /// a default "cache" folder is resolved next to the settings file (or via the
        /// SPOOL_CACHE_DIR environment variable).
        /// </summary>
        public string CacheDirectory { get; set; } = string.Empty;

        /// <summary>
        /// Number of consecutive connection failures to a BitTorrent server before its
        /// streams are marked as Error (and polling for them stops). A fresh engine run
        /// resets the counters and re-activates errored streams.
        /// </summary>
        [Range(1, 10)]
        public int ServerRetryCount { get; set; } = 3;

        /// <summary>
        /// Percentage of the spooling cap reserved as headroom for BitTorrent "boundary
        /// piece" overhead. When a selected file shares a piece with a skipped file,
        /// qbitorrent must still download that whole piece and writes the skipped portion
        /// to a transient ".parts" file. That data is real disk usage that is NOT counted
        /// in the selected files' sizes, so it can push a batch over the cap.
        ///
        /// Example: a 5% margin on a 1TB cap reserves 50GB of headroom, so the engine only
        /// allocates up to 950GB of selected files, leaving room for the .parts overhead.
        /// </summary>
        [Range(0, 20)]
        public int SpoolingCapSafetyMarginPercent { get; set; } = 5;

        /// <summary>
        /// Whether the web UI shows a confirmation dialog before cancelling/removing a
        /// stream. When true, the delete action asks for confirmation (with a "don't ask
        /// again" checkbox); when false, the action proceeds immediately.
        /// </summary>
        public bool ConfirmDeleteConfirmation { get; set; } = true;
    }
}
