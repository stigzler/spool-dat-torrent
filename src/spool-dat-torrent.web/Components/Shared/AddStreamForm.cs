namespace SpoolDatTorrent.Web.Components.Shared
{
    /// <summary>
    /// Data collected by <see cref="AddStreamDialog"/> and passed back to the caller.
    /// </summary>
    public class AddStreamForm
    {
        public string? Name { get; set; }
        public string? Server { get; set; }
        public string? Target { get; set; }
        public string Strategy { get; set; } = string.Empty;

        /// <summary>Optional per-stream settling time (seconds). Null inherits the global default.</summary>
        public int? SettlingTimeSeconds { get; set; }

        /// <summary>Comma-separated filename substrings to download first (e.g. "(USA),(Europe)").</summary>
        public string? PriorityTerms { get; set; }

        /// <summary>Comma-separated filename substrings to download last (e.g. "(Japan),(China)").</summary>
        public string? DePriorityTerms { get; set; }

        /// <summary>Optional per-stream spooling cap (GB). Null inherits the fair-share split.</summary>
        public long? SpoolingCapGb { get; set; }

        /// <summary>Server-side path to the uploaded .torrent file (if the user uploaded one).</summary>
        public string? TorrentFilePath { get; set; }

        /// <summary>Original filename of the uploaded .torrent file (for display / default name).</summary>
        public string? OriginalTorrentName { get; set; }

        /// <summary>Server-side path to the uploaded .dat file.</summary>
        public string? DatFilePath { get; set; }

        /// <summary>Original filename of the uploaded DAT file (for display).</summary>
        public string? OriginalDatName { get; set; }

        /// <summary>Magnet link or info-hash (alternative to uploading a .torrent file).</summary>
        public string? Magnet { get; set; }

        /// <summary>Set when an error occurred before the dialog closed (e.g. upload failure).</summary>
        public string? Error { get; set; }
    }
}
