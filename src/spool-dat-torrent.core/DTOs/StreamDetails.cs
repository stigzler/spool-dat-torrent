namespace SpoolDatTorrent.Core.DTOs
{
    /// <summary>
    /// A read-only summary of a single spooling stream, used by host applications
    /// (CLI, Docker web UI, desktop) to list ongoing streams.
    /// </summary>
    public class StreamDetails
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string TorrentIdentifier { get; set; } = string.Empty;
        public string DatFilePath { get; set; } = string.Empty;
        public string? OriginalDatPath { get; set; }
        public string? SpoolingTargetOverride { get; set; }
        public string ServerProfileId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public System.DateTime CreatedUtc { get; set; }

        /// <summary>Comma-separated filename substrings to download first.</summary>
        public string PriorityTerms { get; set; } = string.Empty;

        /// <summary>Comma-separated filename substrings to download last.</summary>
        public string DePriorityTerms { get; set; } = string.Empty;

        /// <summary>Number of desired (DAT-matched) files already moved to the destination.</summary>
        public int MovedCount { get; set; }

        /// <summary>Total number of desired (DAT-matched) files in the torrent.</summary>
        public int TotalCount { get; set; }

        /// <summary>Progress of the whole job as a fraction (0.0 - 1.0).</summary>
        public double Progress => TotalCount == 0 ? 0 : (double)MovedCount / TotalCount;
    }
}
