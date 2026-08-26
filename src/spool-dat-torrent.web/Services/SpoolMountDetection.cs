using SpoolDatTorrent.Core.Configuration;

namespace SpoolDatTorrent.Web.Services
{
    /// <summary>
    /// Detects whether the optional library destination is available.
    ///
    /// The library is an optional second destination root. In a Docker container it is only
    /// available when the user has mounted a volume at /library-dir in their compose file
    /// (e.g. "- /mnt/pool/media/games/roms:/library-dir"). We detect that mount so the UI can
    /// hide/disable library controls when it is absent. For local (non-Docker) development,
    /// an explicit LibraryDir value in config.json is treated as "available".
    /// </summary>
    public static class SpoolMountDetection
    {
        /// <summary>
        /// True when the library destination should be offered. Either the user configured an
        /// explicit LibraryDir (local dev), or the /library-dir mount is present (container).
        /// </summary>
        public static bool IsLibraryAvailable(GlobalSpoolSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.LibraryDir))
            {
                return true;
            }

            return IsMounted(SpoolPaths.DefaultLibraryDir);
        }

        /// <summary>
        /// Check whether a path is an actual mount point by reading /proc/self/mountinfo.
        /// Returns false on non-Linux (e.g. Windows dev), where there is no mountinfo.
        /// </summary>
        private static bool IsMounted(string path)
        {
            const string mountInfoPath = "/proc/self/mountinfo";
            if (!File.Exists(mountInfoPath))
            {
                return false;
            }

            var normalized = path.TrimEnd('/');
            foreach (var line in File.ReadLines(mountInfoPath))
            {
                // mountinfo fields are space-separated; the mount point is field index 4.
                var parts = line.Split(' ');
                if (parts.Length >= 5 && parts[4] == normalized)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
