using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SpoolDatTorrent.Core.Configuration;
using SpoolDatTorrent.Core.Data;
using SpoolDatTorrent.Core.DTOs;
using SpoolDatTorrent.Core.Helpers;
using SpoolDatTorrent.Core.Interfaces;
using SpoolDatTorrent.Core.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using static SpoolDatTorrent.Core.Helpers.Logger;

namespace SpoolDatTorrent.Core.Services
{
    public class SpoolingEngine : BackgroundService
    {
        private readonly IBitTorrentClientFactory _clientFactory;
        private readonly IDatParserService _datParser;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly GlobalSpoolSettings _settings;
        private readonly ISpoolingProgressReporter? _progressReporter;

        // Cache the DAT game names per stream so we don't re-parse the XML on every poll.
        private readonly ConcurrentDictionary<string, HashSet<string>> _datCache = new(StringComparer.OrdinalIgnoreCase);

        // Latest per-stream progress snapshots, keyed by torrent identifier. Emitted to the
        // optional progress reporter at the end of each evaluation cycle.
        private readonly ConcurrentDictionary<string, StreamProgressInfo> _progressSnapshots = new(StringComparer.OrdinalIgnoreCase);

        // Cache of file indices already moved to the destination, keyed by torrent identifier.
        // Once a file is confirmed moved (exists at destination with correct size), we stop
        // re-statting it on every poll cycle — this is the dominant per-cycle disk cost for
        // large torrents (thousands of files). A file is added here when first detected as
        // moved, or when we successfully copy it.
        private readonly ConcurrentDictionary<string, HashSet<int>> _movedFileCache = new(StringComparer.OrdinalIgnoreCase);

        public SpoolingEngine(
            IServiceScopeFactory scopeFactory,
            IBitTorrentClientFactory clientFactory,
            IDatParserService datParser,
            IOptions<GlobalSpoolSettings> settings,
            ISpoolingProgressReporter? progressReporter = null)
        {
            _scopeFactory = scopeFactory;
            _clientFactory = clientFactory;
            _datParser = datParser;
            _settings = settings.Value;
            _progressReporter = progressReporter;
        }

        // Used by future Desktop App: Can be called on-demand by a UI button or on app startup
        public async Task EvaluateAllStreamsAsync(CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<SpoolDbContext>();

            // 1. Fetch ALL streams (for reporting) and the Active subset (for processing).
            //    Reporting the full list lets a dashboard show every job, including ones
            //    that are paused/completed or not yet touched this run.
            var allStreams = await dbContext.Streams.ToListAsync(cancellationToken);
            var activeStreams = allStreams
                .Where(s => s.Status == StreamLifecycleStatus.Active)
                .ToList();

            // 2. Group the active streams by their designated Server Profile
            var streamsByServer = activeStreams.GroupBy(s =>
                string.IsNullOrWhiteSpace(s.ServerProfileId) ? _settings.DefaultServerProfile : s.ServerProfileId);

            // 3. Process each server independently
            foreach (var serverGroup in streamsByServer)
            {
                var profileName = serverGroup.Key;
                var streamsOnThisServer = serverGroup.ToList();

                // Check if the profile exists in settings to get the cap
                if (!_settings.TorrentServers.TryGetValue(profileName, out var profileSettings))
                {
                    Console.WriteLine($"Skipping unknown profile: {profileName}");
                    continue;
                }

                // Calculate the fair split of the cap for this specific server (converting GB to Bytes)
                long serverCapBytes = profileSettings.SpoolingCapGb * 1024L * 1024L * 1024L;
                long capPerStream = serverCapBytes / streamsOnThisServer.Count;

                // Apply the safety margin: reserve a percentage of the cap for BitTorrent
                // "boundary piece" overhead (the transient .parts file). Without this, a
                // batch can exceed the cap because libtorrent downloads whole pieces that
                // straddle selected/skipped file boundaries.
                capPerStream = ApplySafetyMargin(capPerStream);

                // Get the authenticated client for this specific server
                var torrentClient = _clientFactory.GetClient(profileName);

                try
                {
                    await torrentClient.AuthenticateAsync(cancellationToken);

                    // Process each stream on this server
                    foreach (var stream in streamsOnThisServer)
                    {
                        await ProcessStreamAsync(stream, capPerStream, profileSettings, torrentClient, dbContext, cancellationToken);
                    }
                }
                catch (HttpRequestException ex)
                {
                    // Client unreachable (e.g. qBittorrent is down). Log and retry next cycle
                    // rather than crashing the host (CLI, Docker service, or desktop app).
                    LogStatus($"BitTorrent client '{profileName}' unreachable: {ex.Message}. Will retry next cycle.");
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Client timed out. Same treatment: log and retry next cycle.
                    LogStatus($"BitTorrent client '{profileName}' timed out. Will retry next cycle.");
                }
            }

            // 4. Report the full stream list (all jobs, not just the processed ones) so a
            //    dashboard can render every job with its latest known progress.
            ReportAllStreams(allStreams);
        }

        private void LogStatus(string message)
        {
            // Always write to the log file.
            Logger.Log(message, echoToConsole: false);

            if (_progressReporter != null)
            {
                // Route through the reporter so a live display can render it cleanly.
                // Writing to the raw console here would corrupt Spectre's Live output.
                _progressReporter.ReportStatus(message);
            }
            else
            {
                // No live display attached (e.g. Docker service): echo to the console.
                Console.WriteLine(message);
            }
        }

        private void ReportStreamSnapshot(StreamProgressInfo snapshot)
        {
            _progressSnapshots[snapshot.TorrentIdentifier] = snapshot;
        }

        private void ReportAllStreams(IReadOnlyList<TorrentStreamItem> allStreams)
        {
            if (_progressReporter == null) return;

            var list = allStreams
                .Select(s => _progressSnapshots.TryGetValue(s.TorrentIdentifier, out var snap)
                    ? snap
                    : new StreamProgressInfo { Name = s.Name, TorrentIdentifier = s.TorrentIdentifier, Status = s.Status.ToString() })
                .ToList();

            _progressReporter.ReportStreams(list);
        }

        private HashSet<int> GetMovedFileCache(string torrentIdentifier)
        {
            return _movedFileCache.GetOrAdd(torrentIdentifier, _ => new HashSet<int>());
        }

        private bool IsAlreadyMoved(string torrentIdentifier, TorrentFileDto file, string destinationRoot, string prefixToStrip)
        {
            var cache = GetMovedFileCache(torrentIdentifier);

            // Fast path: we already confirmed this file is moved — skip the disk stat.
            if (cache.Contains(file.Index))
            {
                return true;
            }

            // Slow path (first time only): stat the destination to confirm presence + size.
            string destPath = GetDestinationPath(destinationRoot, prefixToStrip, file.Name);
            bool isSpooled = File.Exists(destPath) && new FileInfo(destPath).Length == file.Size;

            if (isSpooled)
            {
                cache.Add(file.Index);
            }

            return isSpooled;
        }

        private void MarkMoved(string torrentIdentifier, int fileIndex)
        {
            GetMovedFileCache(torrentIdentifier).Add(fileIndex);
        }

        private string TranslateToLocalPath(string torrentSavePath, string fileRelativeName, TorrentServerProfile profile)
        {
            // 1. Combine qBittorrent's root save directory with the relative file path
            string absoluteReportedPath = Path.Combine(torrentSavePath, fileRelativeName);

            // 2. Apply container mapping if it exists
            if (profile.ClientDownloadsMapping != null &&
                !string.IsNullOrWhiteSpace(profile.ClientDownloadsMapping.ClientVirtualPrefix) &&
                !string.IsNullOrWhiteSpace(profile.ClientDownloadsMapping.AppVirtualPrefix))
            {
                string normalizedReported = absoluteReportedPath.Replace('\\', '/');
                string normalizedClientPrefix = profile.ClientDownloadsMapping.ClientVirtualPrefix.Replace('\\', '/');

                if (normalizedReported.StartsWith(normalizedClientPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    string relativeMappedPath = normalizedReported.Substring(normalizedClientPrefix.Length).TrimStart('/');
                    absoluteReportedPath = Path.Combine(profile.ClientDownloadsMapping.AppVirtualPrefix, relativeMappedPath);
                }
            }

            return absoluteReportedPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        }

        private async Task CopyAndVerifyAsync(string sourcePath, string destinationPath, long expectedSize, CancellationToken cancellationToken)
        {
            var srcInfo = new FileInfo(sourcePath);

            // Defend against 0 KB shells: The scratch file MUST match the API expected size before we even touch it
            if (!srcInfo.Exists || srcInfo.Length != expectedSize)
            {
                throw new IOException($"Source file is not fully formed. Expected: {expectedSize}, Actual: {(srcInfo.Exists ? srcInfo.Length : 0)}");
            }

            // Ensure the destination directory (including any subfolder structure) exists
            var destDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var destStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await sourceStream.CopyToAsync(destStream, 81920, cancellationToken);
            }

            var destInfo = new FileInfo(destinationPath);
            if (destInfo.Length != expectedSize)
            {
                File.Delete(destinationPath);
                throw new IOException($"Destination verification failed. Expected: {expectedSize}, Actual: {destInfo.Length}");
            }
        }

        // Used by Web/Docker: Runs continuously in the background
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await EvaluateAllStreamsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"SpoolingEngine encountered an error: {ex.Message}");
                }

                await Task.Delay(TimeSpan.FromSeconds(_settings.PollIntervalSeconds), stoppingToken);
            }
        }

        private async Task ProcessStreamAsync(
                            TorrentStreamItem stream,
                            long allocatedCapBytes,
                            TorrentServerProfile profileSettings,
                            IBitTorrentClient torrentClient,
                            SpoolDbContext dbContext,
                            CancellationToken cancellationToken)
        {
            var desiredGames = await GetDesiredGamesAsync(stream.DatFilePath, cancellationToken);

            // RECOVERY: if the torrent is missing from the client (e.g. the app was closed
            // mid-rebuild, after delete but before re-add), re-add it and re-allocate so we
            // pick up where we left off. Already-moved files are detected by their presence
            // on disk, so nothing is re-downloaded.
            if (!await torrentClient.TorrentExistsAsync(stream.TorrentIdentifier, cancellationToken))
            {
                await RecoverMissingTorrentAsync(stream, desiredGames, allocatedCapBytes, torrentClient, cancellationToken);
                return;
            }

            string torrentSavePath = await torrentClient.GetTorrentSavePathAsync(stream.TorrentIdentifier, cancellationToken);
            string torrentName = await torrentClient.GetTorrentNameAsync(stream.TorrentIdentifier, cancellationToken);
            var torrentFiles = await torrentClient.GetFilesAsync(stream.TorrentIdentifier, cancellationToken);

            if (torrentFiles == null || !torrentFiles.Any()) return;

            // Destination root resolution:
            //   - Explicit per-stream target (SpoolingTargetOverride): files go directly
            //     into it (no torrent-name subfolder).
            //   - Otherwise: [DefaultSpoolingTarget]/[torrent name].
            // The torrent's internal subfolder structure is preserved in both cases.
            string destinationRoot = GetDestinationRoot(stream, torrentName);
            string prefixToStrip = GetPrefixToStrip(stream, torrentName, torrentFiles);

            // Only consider files that the DAT actually wants
            var desiredFiles = torrentFiles
                .Where(f => desiredGames.Contains(Path.GetFileNameWithoutExtension(f.Name)))
                .ToList();

            var alreadyMoved = new List<TorrentFileDto>();
            var readyToMove = new List<TorrentFileDto>();
            var downloading = new List<TorrentFileDto>();
            var pending = new List<TorrentFileDto>();

            foreach (var file in desiredFiles)
            {
                bool isSpooled = IsAlreadyMoved(stream.TorrentIdentifier, file, destinationRoot, prefixToStrip);

                if (isSpooled)
                {
                    alreadyMoved.Add(file);
                }
                else if (file.Progress >= 1.0f && file.Priority > 0)
                {
                    readyToMove.Add(file);
                }
                else if (file.Priority > 0 && file.Progress > 0)
                {
                    downloading.Add(file);
                }
                else if (file.Priority > 0)
                {
                    // Priority > 0 but progress == 0: not started yet (first run / freshly re-added)
                    pending.Add(file);
                }
                // else: priority == 0 => skipped, ignore
            }

            // Emit a progress snapshot for this stream: overall job progress (moved vs
            // total desired) plus the current batch's files and their download progress.
            var snapshot = new StreamProgressInfo
            {
                Name = stream.Name,
                TorrentIdentifier = stream.TorrentIdentifier,
                Status = stream.Status.ToString(),
                MovedCount = alreadyMoved.Count,
                TotalCount = desiredFiles.Count,
                Files = downloading
                    .Concat(readyToMove)
                    .Select(f => new FileProgressInfo { Name = Path.GetFileName(f.Name), Progress = f.Progress })
                    .ToList()
            };
            ReportStreamSnapshot(snapshot);

            // Persist progress to the DB so it survives app restarts and is queryable by
            // the list command / web UI even when the engine is not running.
            stream.MovedCount = alreadyMoved.Count;
            stream.TotalCount = desiredFiles.Count;

            // STATE: WAIT — files are actively downloading. Do nothing until the whole
            // batch completes, so we never delete the torrent mid-download.
            LogStatus($"Awaiting completion for {downloading.Count} file/s.");
            if (downloading.Any())
            {
                var inProgress = downloading.Select(f => $"{Path.GetFileName(f.Name)} ({f.Progress:P})");
                Logger.Log($"[Spooling] Batch active. Waiting for {downloading.Count} files: {string.Join(", ", inProgress)}", echoToConsole: false);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            // STATE: DRAIN — the batch finished. Copy completed files, then delete the
            // torrent and re-add it with the next batch's priorities. This is the key fix:
            // we never delete an individual file out from under libtorrent (which causes
            // "file_open" errors). Instead we delete the WHOLE torrent (deleteFiles=true)
            // and re-add the SAME .torrent (same info-hash, same swarm), letting libtorrent
            // rebuild boundary pieces into .parts files for the files we skip.
            if (readyToMove.Any())
            {
                LogStatus($"Halting torrent to move {readyToMove.Count} completed files...");
                await torrentClient.PauseTorrentAsync(stream.TorrentIdentifier, cancellationToken);
                await Task.Delay(1000, cancellationToken);

                var copiedIndices = new List<int>();

                foreach (var file in readyToMove)
                {
                    string destinationPath = GetDestinationPath(destinationRoot, prefixToStrip, file.Name);
                    string sourcePath = TranslateToLocalPath(torrentSavePath, file.Name, profileSettings);

                    LogStatus($"Moving file: {Path.GetFileName(file.Name)}...");

                    try
                    {
                        await CopyAndVerifyAsync(sourcePath, destinationPath, file.Size, cancellationToken);
                        copiedIndices.Add(file.Index);
                        MarkMoved(stream.TorrentIdentifier, file.Index);
                    }
                    catch (IOException ex)
                    {
                        LogStatus($"Move failed (will retry next loop)");
                    }
                }

                if (copiedIndices.Any())
                {
                    LogStatus($"Moved {copiedIndices.Count} files. Rebuilding torrent for next batch...");
                    await RebuildTorrentForNextBatchAsync(stream, torrentSavePath, torrentName, torrentFiles, profileSettings, desiredGames, allocatedCapBytes, torrentClient, cancellationToken);
                }
                else
                {
                    // Nothing copied this pass (all failed) — resume and retry next loop
                    await torrentClient.ResumeTorrentAsync(stream.TorrentIdentifier, cancellationToken);
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            // STATE: ALLOCATE — nothing downloading and nothing completed. Either first run
            // (torrent added paused) or a freshly re-added torrent. Set priorities and resume.
            if (pending.Any())
            {
                LogStatus($"Constructing next batch to fit into maximum spool size ({allocatedCapBytes.ToGigabytes()} GB)...");
                await AllocateBatchAsync(stream, torrentFiles, desiredGames, alreadyMoved, allocatedCapBytes, torrentClient, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            // STATE: COMPLETE — nothing left to download or move
            LogStatus($"Stream '{stream.Name}' completed!");
            stream.Status = StreamLifecycleStatus.Completed;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        private static string SanitizeFolderName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
            return string.IsNullOrWhiteSpace(sanitized) ? "stream" : sanitized;
        }

        private string GetDestinationRoot(TorrentStreamItem stream, string torrentName)
        {
            // Explicit per-stream target: files go directly into it (no torrent-name subfolder).
            if (!string.IsNullOrWhiteSpace(stream.SpoolingTargetOverride))
            {
                return stream.SpoolingTargetOverride;
            }

            // Automated: [DefaultSpoolingTarget]/[torrent name].
            // Fall back to the info-hash if the torrent name is unavailable.
            string torrentFolder = string.IsNullOrWhiteSpace(torrentName)
                ? stream.TorrentIdentifier
                : torrentName;

            return Path.Combine(_settings.DefaultSpoolingTarget, SanitizeFolderName(torrentFolder));
        }

        private static string GetDestinationPath(string destinationRoot, string prefixToStrip, string fileRelativeName)
        {
            return Path.Combine(destinationRoot, StripPrefix(fileRelativeName, prefixToStrip));
        }

        private static string StripPrefix(string fileRelativeName, string prefixToStrip)
        {
            string relative = fileRelativeName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

            // An empty prefix means "no common root" (files scattered across unrelated
            // top-level folders). In that case we do NOT flatten: we return the full
            // relative path so the torrent's own structure is mirrored under the target.
            if (string.IsNullOrWhiteSpace(prefixToStrip))
            {
                return relative;
            }

            var prefixSegments = prefixToStrip.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            var segments = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).ToList();

            if (segments.Count > prefixSegments.Length)
            {
                bool matches = true;
                for (int i = 0; i < prefixSegments.Length; i++)
                {
                    if (!string.Equals(segments[i], prefixSegments[i], StringComparison.OrdinalIgnoreCase))
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    segments.RemoveRange(0, prefixSegments.Length);
                    return string.Join(Path.DirectorySeparatorChar, segments);
                }
            }

            return relative;
        }

        private static string GetCommonRootDirectory(IReadOnlyList<TorrentFileDto> files)
        {
            // Compute the deepest directory prefix shared by all files in the torrent.
            // This is the "common root" stripped when an explicit target is given, so files
            // land directly in the target while preserving any subfolders that exist below
            // that common root (e.g. ".../Aftermarket/...").
            var dirSegments = files
                .Select(f => GetDirectorySegments(f.Name))
                .ToList();

            if (dirSegments.Count == 0)
            {
                return string.Empty;
            }

            var first = dirSegments[0];
            int commonCount = first.Count;

            foreach (var segs in dirSegments.Skip(1))
            {
                int i = 0;
                while (i < commonCount && i < segs.Count && string.Equals(first[i], segs[i], StringComparison.OrdinalIgnoreCase))
                {
                    i++;
                }
                commonCount = i;
                if (commonCount == 0) break;
            }

            return string.Join(Path.DirectorySeparatorChar, first.Take(commonCount));
        }

        private static List<string> GetDirectorySegments(string filePath)
        {
            var normalized = filePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            var segments = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).ToList();

            // Drop the filename (last segment) so we only compare directory structure.
            if (segments.Count > 0)
            {
                segments.RemoveAt(segments.Count - 1);
            }

            return segments;
        }

        private string GetPrefixToStrip(TorrentStreamItem stream, string torrentName, IReadOnlyList<TorrentFileDto> files)
        {
            // Explicit target: strip the common root directory so files land directly in
            // the target, preserving only subfolders below that common root.
            if (!string.IsNullOrWhiteSpace(stream.SpoolingTargetOverride))
            {
                return GetCommonRootDirectory(files);
            }

            // Automated: strip only the torrent name (first segment) to avoid duplicating
            // it with the [torrent name] folder in the destination root.
            return torrentName;
        }

        private long ApplySafetyMargin(long capBytes)
        {
            // Reserve a percentage of the cap for BitTorrent "boundary piece" overhead.
            // When a selected file shares a piece with a skipped file, libtorrent downloads
            // the whole piece and writes the skipped portion to a transient ".parts" file.
            // That data is real disk usage not counted in the selected files' sizes, so we
            // shrink the effective cap to leave headroom for it.
            double margin = _settings.SpoolingCapSafetyMarginPercent;

            // Clamp to a sane range (0% - 50%) to guard against misconfiguration.
            if (margin < 0) margin = 0;
            if (margin > 50) margin = 50;

            double factor = 1.0 - (margin / 100.0);
            return (long)(capBytes * factor);
        }

        private async Task<HashSet<string>> GetDesiredGamesAsync(string datFilePath, CancellationToken cancellationToken)
        {
            if (_datCache.TryGetValue(datFilePath, out var cached))
            {
                return cached;
            }

            var games = await _datParser.GetGameNamesFromFileAsync(datFilePath, cancellationToken);
            _datCache[datFilePath] = games;
            return games;
        }

        private async Task AllocateBatchAsync(
            TorrentStreamItem stream,
            IReadOnlyList<TorrentFileDto> torrentFiles,
            HashSet<string> desiredGames,
            List<TorrentFileDto> alreadyMoved,
            long allocatedCapBytes,
            IBitTorrentClient torrentClient,
            CancellationToken cancellationToken)
        {
            long currentFootprint = 0;
            var filesToDownload = new List<int>();
            var filesToSkip = new List<int>();

            foreach (var file in torrentFiles)
            {
                if (alreadyMoved.Any(f => f.Index == file.Index))
                {
                    // Already spooled — keep it at priority 0 so it isn't re-downloaded
                    if (file.Priority != 0) filesToSkip.Add(file.Index);
                    continue;
                }

                // Only download files the DAT actually wants
                if (!desiredGames.Contains(Path.GetFileNameWithoutExtension(file.Name)))
                {
                    filesToSkip.Add(file.Index);
                    continue;
                }

                if (currentFootprint + file.Size <= allocatedCapBytes)
                {
                    filesToDownload.Add(file.Index);
                    currentFootprint += file.Size;
                }
                else
                {
                    filesToSkip.Add(file.Index);
                }
            }

            if (filesToSkip.Any()) await torrentClient.SetFilePrioritiesAsync(stream.TorrentIdentifier, filesToSkip, 0, cancellationToken);
            if (filesToDownload.Any()) await torrentClient.SetFilePrioritiesAsync(stream.TorrentIdentifier, filesToDownload, 1, cancellationToken);

            await torrentClient.ResumeTorrentAsync(stream.TorrentIdentifier, cancellationToken);
        }

        private async Task RebuildTorrentForNextBatchAsync(
            TorrentStreamItem stream,
            string torrentSavePath,
            string torrentName,
            IReadOnlyList<TorrentFileDto> torrentFiles,
            TorrentServerProfile profileSettings,
            HashSet<string> desiredGames,
            long allocatedCapBytes,
            IBitTorrentClient torrentClient,
            CancellationToken cancellationToken)
        {
            // 1. Delete the whole torrent AND its downloaded data. This is safe because we
            //    have already copied every completed file to its final destination.
            await torrentClient.DeleteTorrentAsync(stream.TorrentIdentifier, deleteFiles: true, cancellationToken);

            // 1b. qBittorrent deletes files asynchronously. Wait until the scratch files are
            //     actually gone from disk before re-adding, otherwise the re-add finds stale
            //     files and triggers a slow hash check ("checking" phase).
            await WaitForScratchFilesDeletedAsync(torrentSavePath, torrentFiles, profileSettings, cancellationToken);

            // 2. Re-add the SAME torrent source (same info-hash => same swarm), paused.
            string? source = !string.IsNullOrWhiteSpace(stream.OriginalTorrentPath)
                ? stream.OriginalTorrentPath
                : stream.OriginalMagnet;

            if (string.IsNullOrWhiteSpace(source))
            {
                LogStatus($"Cannot rebuild torrent: no original .torrent path or magnet stored for stream '{stream.Name}'.");
                return;
            }

            await torrentClient.AddTorrentAsync(source, torrentSavePath, addPaused: true, cancellationToken);

            // 3. Poll until qBittorrent has parsed the file tree (metadata ready) instead of
            //    a fixed delay. This is faster for small torrents and safer for huge ones.
            IReadOnlyList<TorrentFileDto>? freshFiles = null;
            for (int i = 0; i < 60; i++)
            {
                await Task.Delay(500, cancellationToken);
                freshFiles = await torrentClient.GetFilesAsync(stream.TorrentIdentifier, cancellationToken);
                if (freshFiles != null && freshFiles.Count > 0)
                {
                    break;
                }
            }

            if (freshFiles == null || freshFiles.Count == 0)
            {
                LogStatus($"Torrent re-added but file list never became available for '{stream.Name}'.");
                return;
            }

            var alreadyMoved = new List<TorrentFileDto>();
            string destinationRoot = GetDestinationRoot(stream, torrentName);
            string prefixToStrip = GetPrefixToStrip(stream, torrentName, freshFiles);

            foreach (var file in freshFiles)
            {
                if (IsAlreadyMoved(stream.TorrentIdentifier, file, destinationRoot, prefixToStrip))
                {
                    alreadyMoved.Add(file);
                }
            }

            await AllocateBatchAsync(stream, freshFiles, desiredGames, alreadyMoved, allocatedCapBytes, torrentClient, cancellationToken);
        }

        private async Task RecoverMissingTorrentAsync(
            TorrentStreamItem stream,
            HashSet<string> desiredGames,
            long allocatedCapBytes,
            IBitTorrentClient torrentClient,
            CancellationToken cancellationToken)
        {
            string? source = !string.IsNullOrWhiteSpace(stream.OriginalTorrentPath)
                ? stream.OriginalTorrentPath
                : stream.OriginalMagnet;

            if (string.IsNullOrWhiteSpace(source))
            {
                LogStatus($"Cannot Load torrent file for Stream: '{stream.Name}'.");
                return;
            }

            LogStatus($"Re-adding Torrent to resume stream '{stream.Name}'...");

            // Re-add paused. We don't know the original save path, so let qBittorrent use
            // its default (or the profile's configured location).
            await torrentClient.AddTorrentAsync(source, savePath: null, addPaused: true, cancellationToken);

            // Poll until the file tree is available.
            IReadOnlyList<TorrentFileDto>? freshFiles = null;
            for (int i = 0; i < 60; i++)
            {
                await Task.Delay(500, cancellationToken);
                freshFiles = await torrentClient.GetFilesAsync(stream.TorrentIdentifier, cancellationToken);
                if (freshFiles != null && freshFiles.Count > 0)
                {
                    break;
                }
            }

            if (freshFiles == null || freshFiles.Count == 0)
            {
                LogStatus($"Torrent re-added but file list never became available for '{stream.Name}'.");
                return;
            }

            string torrentName = await torrentClient.GetTorrentNameAsync(stream.TorrentIdentifier, cancellationToken);
            string destinationRoot = GetDestinationRoot(stream, torrentName);
            string prefixToStrip = GetPrefixToStrip(stream, torrentName, freshFiles);

            var alreadyMoved = new List<TorrentFileDto>();
            foreach (var file in freshFiles)
            {
                if (IsAlreadyMoved(stream.TorrentIdentifier, file, destinationRoot, prefixToStrip))
                {
                    alreadyMoved.Add(file);
                }
            }

            await AllocateBatchAsync(stream, freshFiles, desiredGames, alreadyMoved, allocatedCapBytes, torrentClient, cancellationToken);
        }

        private async Task WaitForScratchFilesDeletedAsync(
            string torrentSavePath,
            IReadOnlyList<TorrentFileDto> torrentFiles,
            TorrentServerProfile profileSettings,
            CancellationToken cancellationToken)
        {
            // The files we care about are the ones that were actually downloaded (progress >= 1).
            // We wait until none of them exist on the scratch drive anymore.
            var downloadedPaths = torrentFiles
                .Where(f => f.Progress >= 1.0f)
                .Select(f => TranslateToLocalPath(torrentSavePath, f.Name, profileSettings))
                .ToList();

            if (downloadedPaths.Count == 0)
            {
                return;
            }

            for (int i = 0; i < 60; i++)
            {
                if (downloadedPaths.All(p => !File.Exists(p)))
                {
                    return;
                }
                await Task.Delay(1000, cancellationToken);
            }

            LogStatus("Scratch files still present after 60s; re-add may trigger a hash check.");
        }
    }
}
