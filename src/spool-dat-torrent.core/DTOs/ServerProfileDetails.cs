namespace SpoolDatTorrent.Core.DTOs
{
    /// <summary>
    /// A read-only summary of a configured BitTorrent server profile.
    /// </summary>
    public class ServerProfileDetails
    {
        public string Name { get; set; } = string.Empty;
        public string ClientType { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public bool HasApiKey { get; set; }
        public long SpoolingCapGb { get; set; }
    }
}
