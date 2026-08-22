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
    }
}
