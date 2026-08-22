using System;
using System.Collections.Generic;
using System.Text;

namespace SpoolDatTorrent.Core.Models
{
    public enum FileDownloadStatus
    {
        Pending,
        Downloading,
        Completed,
        Moved,
        Skipped
    }
}
