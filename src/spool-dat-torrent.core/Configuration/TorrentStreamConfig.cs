using System;
using System.Collections.Generic;
using System.Text;

namespace SpoolDatTorrent.Core.Configuration
{
    public class TorrentStreamConfig
    {
        public string Name { get; set; } = string.Empty;
        public string TorrentIdentifier { get; set; } = string.Empty;
        public string DatFilePath { get; set; } = string.Empty;
        public string? SpoolingTargetOverride { get; set; }
        public string? ServerProfileOverride { get; set; } // Null falls back to DefaultServerProfile
        public SpoolingStrategy Strategy { get; set; } = SpoolingStrategy.MoveFiles;
        public string FileFilter { get; set; } = "*.*";

        /// <summary>Per-stream settling time (seconds). Null falls back to the global default.</summary>
        public int? SettlingTimeSeconds { get; set; }

        /// <summary>Comma-separated filename substrings to download first (e.g. "(USA),(Europe)").</summary>
        public string PriorityTerms { get; set; } = string.Empty;

        /// <summary>Comma-separated filename substrings to download last (e.g. "(Japan),(China)").</summary>
        public string DePriorityTerms { get; set; } = string.Empty;
    }
}
