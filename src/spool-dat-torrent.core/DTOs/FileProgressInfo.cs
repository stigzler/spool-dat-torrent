namespace SpoolDatTorrent.Core.DTOs
{
    /// <summary>
    /// Progress of a single file within the current batch, used by host applications
    /// to render a live status display.
    /// </summary>
    public class FileProgressInfo
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>Download progress as a fraction (0.0 - 1.0).</summary>
        public double Progress { get; set; }
        public int StreamId { get; set; }

        /// <summary>Total size of the file in bytes.</summary>
        public long SizeBytes { get; set; }

        /// <summary>Display status of this file within the batch.</summary>
        public string Status { get; set; } = string.Empty;
    }
}
