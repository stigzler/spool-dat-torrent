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
    }
}
