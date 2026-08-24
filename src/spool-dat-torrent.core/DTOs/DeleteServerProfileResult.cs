using System;
using System.Collections.Generic;
using System.Text;

namespace SpoolDatTorrent.Core.DTOs
{
    /// <summary>
    /// Result of attempting to delete a BitTorrent server profile.
    /// </summary>
    public class DeleteServerProfileResult
    {
        /// <summary>True if the profile was deleted; false if deletion was refused.</summary>
        public bool Success { get; set; }

        /// <summary>Human-readable reason for refusal, or a success confirmation.</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>Names of streams that referenced the profile and blocked deletion.</summary>
        public IReadOnlyList<string>? ReferencingStreams { get; set; }
    }
}
