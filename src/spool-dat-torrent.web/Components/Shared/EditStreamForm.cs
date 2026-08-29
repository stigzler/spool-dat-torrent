using SpoolDatTorrent.Core.Configuration;

namespace SpoolDatTorrent.Web.Components.Shared
{
    /// <summary>
    /// Data collected by <see cref="EditStreamDialog"/> and passed back to the caller.
    /// Extendable: add more editable stream properties here as the dialog grows.
    /// </summary>
    public class EditStreamForm
    {
        public string Name { get; set; } = string.Empty;
        public SpoolingStrategy Strategy { get; set; } = SpoolingStrategy.MoveFiles;

        /// <summary>Per-stream settling time (seconds). Null uses the global default.</summary>
        public int? SettlingTimeSeconds { get; set; }

        public string PriorityTerms { get; set; } = string.Empty;
        public string DePriorityTerms { get; set; } = string.Empty;

        /// <summary>Per-stream spooling cap (GB). Null clears the override (fair-share split).</summary>
        public long? SpoolingCapGb { get; set; }
    }
}
