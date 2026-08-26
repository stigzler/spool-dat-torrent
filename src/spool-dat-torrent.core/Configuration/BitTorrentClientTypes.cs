using System;
using System.Collections.Generic;
using System.Text;

namespace SpoolDatTorrent.Core.Configuration
{
    /// <summary>
    /// Central registry of supported BitTorrent client types. Used by the web UI to
    /// populate the Client Type dropdown and referenced when instantiating a client.
    /// Add new client types here in a single place so every host (web, CLI, desktop)
    /// stays in sync.
    /// </summary>
    public static class BitTorrentClientTypes
    {
        public const string QBittorrent = "qBittorrent";

        /// <summary>Planned but not yet implemented; retained so the dropdown is forward-compatible.</summary>
        public const string Deluge = "Deluge";

        /// <summary>The canonical, ordered list of client types offered to the user.</summary>
        public static readonly IReadOnlyList<string> All = new List<string>
        {
            QBittorrent            
        };
    }
}
