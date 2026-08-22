using System;
using System.Collections.Generic;
using System.Text;

namespace SpoolDatTorrent.Core.DTOs
{
    public class TorrentFileDto
    {
        public int Index { get; set; }
        public string Name { get; set; } = string.Empty;
        public long Size { get; set; }
        public double Progress { get; set; }
        public int Priority { get; set; } // 0 = Do not download, 1 = Normal, 6 = High, 7 = Maximal
        public bool IsSeed { get; set; }
    }
}
