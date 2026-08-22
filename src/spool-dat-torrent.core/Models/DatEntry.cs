using System;
using System.Collections.Generic;
using System.Text;

namespace SpoolDatTorrent.Core.Models
{
    public class DatEntry
    {
        public string GameName { get; set; } = string.Empty;
        public string RomName { get; set; } = string.Empty;
        public long Size { get; set; }
        public string? Crc { get; set; }
        public string? Md5 { get; set; }
        public string? Sha1 { get; set; }
    }
}
