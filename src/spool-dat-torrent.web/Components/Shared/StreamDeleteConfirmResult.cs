namespace SpoolDatTorrent.Web.Components.Shared
{
    /// <summary>
    /// Result returned by <see cref="StreamDeleteConfirmDialog"/>.
    /// </summary>
    public class StreamDeleteConfirmResult
    {
        public bool Confirmed { get; set; }
        public bool DontAskAgain { get; set; }
    }
}
