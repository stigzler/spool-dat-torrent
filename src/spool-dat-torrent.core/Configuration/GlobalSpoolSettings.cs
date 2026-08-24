using System;
using System.Collections.Generic;
using System.Text;

namespace SpoolDatTorrent.Core.Configuration
{
    public class GlobalSpoolSettings
    {
        public string DefaultServerProfile { get; set; } = "LocalQBit";
        public Dictionary<string, TorrentServerProfile> TorrentServers { get; set; } = new();
        public string DefaultSpoolingTarget { get; set; } = string.Empty;
        public int PollIntervalSeconds { get; set; } = 2;
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
        public double SpoolingCapSafetyMarginPercent { get; set; } = 5.0;
    }
}
