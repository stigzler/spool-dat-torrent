using System;
using System.Collections.Generic;
using System.Text;

namespace SpoolDatTorrent.Core.Configuration
{
    public class GlobalSpoolSettings
    {
        public string TorrentClientHost { get; set; } = "http://localhost:8080";
        public string TorrentClientApiKey { get; set; } = string.Empty;
        public long GlobalCapGb { get; set; } = 2000; // e.g., 2TB default scratch cap
        public string DefaultSpoolingTarget { get; set; } = string.Empty;
        public int PollIntervalSeconds { get; set; } = 15;
        public int SettlingTimeSeconds { get; set; } = 30;
    }
}
