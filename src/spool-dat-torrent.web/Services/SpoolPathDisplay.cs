using SpoolDatTorrent.Core.Configuration;

namespace SpoolDatTorrent.Web.Services
{
    /// <summary>
    /// Maps a container-side destination path to its display form for the web UI.
    ///
    /// The container only knows the container-side mount points (/staging-dir, /library-dir).
    /// When the user has configured the corresponding host paths (StagingHostPath /
    /// LibraryHostPath), we replace the container-root prefix with the host path so the UI
    /// shows the user's real filesystem. This is display-only — file operations always use
    /// the container path.
    /// </summary>
    public static class SpoolPathDisplay
    {
        /// <summary>
        /// Convert a container path to its display form. If a host path is configured for the
        /// matching root, the root prefix is replaced; otherwise the container path is returned
        /// unchanged. Returns null for a null/empty input.
        /// </summary>
        public static string? ToDisplayPath(GlobalSpoolSettings settings, string? containerPath)
        {
            if (string.IsNullOrWhiteSpace(containerPath))
            {
                return null;
            }

            var normalized = containerPath.Trim().Replace('\\', '/').TrimEnd('/');
            var stagingRoot = settings.DefaultSpoolingTarget.Trim().Replace('\\', '/').TrimEnd('/');
            var libraryRoot = (string.IsNullOrWhiteSpace(settings.LibraryDir)
                ? SpoolPaths.DefaultLibraryDir
                : settings.LibraryDir).Trim().Replace('\\', '/').TrimEnd('/');

            if (!string.IsNullOrWhiteSpace(stagingRoot)
                && normalized.StartsWith(stagingRoot, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(settings.StagingHostPath))
            {
                return PrefixReplace(normalized, stagingRoot, settings.StagingHostPath);
            }

            if (!string.IsNullOrWhiteSpace(libraryRoot)
                && normalized.StartsWith(libraryRoot, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(settings.LibraryHostPath))
            {
                return PrefixReplace(normalized, libraryRoot, settings.LibraryHostPath);
            }

            return normalized;
        }

        private static string PrefixReplace(string path, string root, string hostPath)
        {
            var remainder = path.Substring(root.Length).TrimStart('/');
            var host = hostPath.Trim().Replace('\\', '/').TrimEnd('/');
            return string.IsNullOrEmpty(remainder) ? host : $"{host}/{remainder}";
        }
    }
}
