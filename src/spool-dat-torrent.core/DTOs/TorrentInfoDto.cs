using System;
using System.Collections.Generic;
using System.Text;

namespace SpoolDatTorrent.Core.DTOs
{
    public class TorrentInfoDto
    {
        public string Hash { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long Size { get; set; }
        public long Downloaded { get; set; }
        public string State { get; set; } = string.Empty;
    }
}
