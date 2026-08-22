using System;
using System.Collections.Generic;
using System.Text;

namespace SpoolDatTorrent.Core.Configuration
{
    public class TorrentStreamConfig
    {
        public string Name { get; set; } = string.Empty;
        public string TorrentIdentifier { get; set; } = string.Empty; // Path, magnet, or hash
        public string DatFilePath { get; set; } = string.Empty;
        public string? SpoolingTargetOverride { get; set; } // Null falls back to global default
        public SpoolingStrategy Strategy { get; set; } = SpoolingStrategy.MoveFiles;
        public string FileFilter { get; set; } = "*.*";
    }
}
