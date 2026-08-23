using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace SpoolDatTorrent.Core.Configuration
{
    /// <summary>
    /// A translation object to translate the bittorrent client's reported download paths into
    /// a SpoolDatTorrent Virtual path. Translations to real system paths as per below eg compose files:
    /// 
    /// services:
    /// qbittorrent:
    ///  image: lscr.io/linuxserver/qbittorrent:latest
    ///  volumes:
    ///    # Bare Metal Path : qBittorrent's Virtual Path
    ///    - /mnt/scratch/qbittorrent/complete:/downloads/complete
    ///
    /// spooldattorrent:
    ///  image: spooldattorrent:latest
    ///  volumes:
    ///    # Bare Metal Path : SDT's Virtual Paths
    ///    - /mnt/scratch/qbittorrent/complete:/app/scratch
    ///    - /mnt/pool/roms/spooled:/app/pool
    ///    
    /// </summary>
    public class ClientDownloadsMapping
    {
        /// <summary>
        /// Virtual path reported by the bittorrent client API (e.g., "/downloads/complete")
        /// </summary>
        public string ClientVirtualPrefix { get; set; } = string.Empty; // e.g., "/downloads" inside Docker

        /// <summary>
        /// The virtual path mapped inside the SDT container (e.g., "/app/scratch")
        /// </summary>
        public string AppVirtualPrefix { get; set; } = string.Empty;
    }
}
