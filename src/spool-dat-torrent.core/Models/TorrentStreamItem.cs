using SpoolDatTorrent.Core.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace SpoolDatTorrent.Core.Models
{
    public class TorrentStreamItem
    {
        public int Id { get; set; }
        public string TorrentIdentifier { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string DatFilePath { get; set; } = string.Empty;
        public string? OriginalTorrentPath { get; set; }
        public string? OriginalMagnet { get; set; }
        public string? SpoolingTargetOverride { get; set; }
        public SpoolingStrategy Strategy { get; set; } = SpoolingStrategy.MoveFiles;
        public string FileFilter { get; set; } = "*.*";
        public StreamLifecycleStatus Status { get; set; } = StreamLifecycleStatus.Active;
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public List<TorrentFileItem> Files { get; set; } = new();
        public string ServerProfileId { get; set; } = string.Empty;

        /// <summary>Number of desired (DAT-matched) files already moved to the destination.</summary>
        public int MovedCount { get; set; }

        /// <summary>Total number of desired (DAT-matched) files in the torrent.</summary>
        public int TotalCount { get; set; }
    }
}
