using System.Linq;
using Microsoft.Extensions.Options;
using SpoolDatTorrent.Core.Configuration;

namespace SpoolDatTorrent.Core.Commands
{
    /// <summary>
    /// Caches per-stream copies of the .torrent and .dat files so a stream remains usable
    /// even if the user later deletes the originals. Reusable by the CLI, Docker web UI,
    /// and desktop apps.
    /// </summary>
    public class StreamFileCache
    {
        private readonly string _cacheDirectory;

        public StreamFileCache(IOptions<GlobalSpoolSettings> settings)
        {
            _cacheDirectory = SettingsManager.GetCacheDirectory(settings.Value.CacheDirectory);
        }

        /// <summary>Full path of the cached .torrent file for a stream (may not exist yet).</summary>
        public string GetCachedTorrentPath(string torrentIdentifier, string originalTorrentPath)
        {
            var name = Path.GetFileName(originalTorrentPath);
            return Path.Combine(_cacheDirectory, torrentIdentifier, name);
        }

        /// <summary>Full path of the cached .dat file for a stream (may not exist yet).</summary>
        public string GetCachedDatPath(string torrentIdentifier, string datFilePath)
        {
            var name = Path.GetFileName(datFilePath);
            return Path.Combine(_cacheDirectory, torrentIdentifier, name);
        }

        /// <summary>
        /// Copy the source .torrent and .dat into the cache for a stream. Returns the
        /// cached paths. Skips copying a source that isn't a local file (e.g. a magnet).
        /// </summary>
        public (string? CachedTorrentPath, string? CachedDatPath) CacheFiles(
            string torrentIdentifier,
            string? originalTorrentPath,
            string datFilePath)
        {
            var dir = Path.Combine(_cacheDirectory, torrentIdentifier);
            Directory.CreateDirectory(dir);

            string? cachedTorrent = null;
            if (!string.IsNullOrWhiteSpace(originalTorrentPath) && File.Exists(originalTorrentPath))
            {
                cachedTorrent = GetCachedTorrentPath(torrentIdentifier, originalTorrentPath);
                File.Copy(originalTorrentPath, cachedTorrent, overwrite: true);
            }

            string? cachedDat = null;
            if (File.Exists(datFilePath))
            {
                cachedDat = GetCachedDatPath(torrentIdentifier, datFilePath);
                File.Copy(datFilePath, cachedDat, overwrite: true);
            }

            return (cachedTorrent, cachedDat);
        }

        /// <summary>Delete the cached files for a stream, and the stream's cache folder.</summary>
        public void DeleteCachedFiles(string? cachedTorrentPath, string? cachedDatPath)
        {
            string? streamDir = null;

            foreach (var path in new[] { cachedTorrentPath, cachedDatPath })
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    // Capture the per-stream folder (the info-hash directory) so we can
                    // remove it once empty.
                    streamDir ??= Path.GetDirectoryName(path);

                    if (File.Exists(path))
                    {
                        try
                        {
                            File.Delete(path);
                        }
                        catch (IOException)
                        {
                            // Best-effort; the folder may be cleaned later.
                        }
                    }
                }
            }

            // Remove the now-empty per-stream folder (e.g. the info-hash directory).
            if (!string.IsNullOrWhiteSpace(streamDir) && Directory.Exists(streamDir))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(streamDir).Any())
                    {
                        Directory.Delete(streamDir);
                    }
                }
                catch (IOException)
                {
                    // Best-effort.
                }
            }
        }
    }
}
