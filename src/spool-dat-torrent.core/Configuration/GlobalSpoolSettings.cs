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
        public int PollIntervalSeconds { get; set; } = 15;
        public int SettlingTimeSeconds { get; set; } = 30;

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
