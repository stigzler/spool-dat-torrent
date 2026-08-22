using System;
using System.Collections.Generic;
using System.Text;

namespace SpoolDatTorrent.Core.Configuration
{
    public class TorrentServerProfile
    {
        public string ClientType { get; set; } = "qBittorrent";
        public string Host { get; set; } = "http://localhost:8080";
        public string Username { get; set; } = "admin";
        public string Password { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public long SpoolingCapGb { get; set; } = 2000;
    }
}
