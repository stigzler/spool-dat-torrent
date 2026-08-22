using System;
using System.Collections.Generic;
using System.Text;

namespace SpoolDatTorrent.Core.Models
{
    public class TorrentFileItem
    {
        public int Id { get; set; }
        public int FileIndex { get; set; }
        public string FileName { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public bool IsMatchedByDat { get; set; }
        public bool IsSelectedForDownload { get; set; }
        public FileDownloadStatus Status { get; set; } = FileDownloadStatus.Pending;
    }
}
