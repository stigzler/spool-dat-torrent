using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace SpoolDatTorrent.Core.Configuration
{
    /// <summary>
    /// Optional translation for DOCKER installs only, where qBittorrent and SpoolDatTorrent
    /// run in separate containers that see the same underlying disk through DIFFERENT mount
    /// points. If the same host folder is mounted at the same path in both containers (the
    /// simplest setup), leave both properties blank — no mapping is needed.
    ///
    /// When they differ (e.g. compose files):
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
        /// The download path as the bittorrent client API reports it (e.g., "/downloads/complete").
        /// </summary>
        public string ClientVirtualPrefix { get; set; } = string.Empty;

        /// <summary>
        /// The path SpoolDatTorrent uses for the SAME location (e.g., "/app/scratch").
        /// Leave both blank when qBittorrent and SDT see the same path.
        /// </summary>
        public string AppVirtualPrefix { get; set; } = string.Empty;
    }
}
