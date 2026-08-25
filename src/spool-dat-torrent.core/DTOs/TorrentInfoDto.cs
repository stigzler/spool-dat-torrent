using System;
using System.Collections.Generic;
using System.Text;

namespace SpoolDatTorrent.Core.DTOs
{
    public class TorrentInfoDto
    {
        public string Hash { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long Size { get; set; }
        public long Downloaded { get; set; }
        public string State { get; set; } = string.Empty;

        /// <summary>Number of connected seeds.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("num_seeds")]
        public int NumSeeds { get; set; }

        /// <summary>Total number of seeds in the swarm.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("num_complete")]
        public int NumComplete { get; set; }

        /// <summary>Number of connected peers (leechers).</summary>
        [System.Text.Json.Serialization.JsonPropertyName("num_leechs")]
        public int NumPeers { get; set; }

        /// <summary>Total number of leechers in the swarm.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("num_incomplete")]
        public int NumIncomplete { get; set; }

        /// <summary>Download speed in bytes/second.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("dlspeed")]
        public long Dlspeed { get; set; }

        /// <summary>ETA in seconds (-1 if unknown).</summary>
        [System.Text.Json.Serialization.JsonPropertyName("eta")]
        public long Eta { get; set; }
    }
}
