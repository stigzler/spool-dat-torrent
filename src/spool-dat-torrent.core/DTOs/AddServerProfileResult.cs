using System;
using System.Collections.Generic;
using System.Text;
using SpoolDatTorrent.Core.Configuration;

namespace SpoolDatTorrent.Core.DTOs
{
    /// <summary>
    /// Result of creating a new BitTorrent server profile.
    /// </summary>
    public class AddServerProfileResult
    {
        /// <summary>True if the profile was created.</summary>
        public bool Success { get; set; }

        /// <summary>Human-readable confirmation message.</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>The name of the created profile (also the dictionary key).</summary>
        public string ProfileName { get; set; } = string.Empty;

        /// <summary>The newly created profile object, for hosts that edit it immediately.</summary>
        public TorrentServerProfile? Profile { get; set; }
    }
}
