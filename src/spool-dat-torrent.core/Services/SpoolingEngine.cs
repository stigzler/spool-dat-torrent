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

        // Latest human-readable status message per stream, keyed by torrent identifier.
        // Updated by LogStatus when a stream context is set; exposed via snapshots so a
        // dashboard can show "what is this stream doing right now".
        private readonly ConcurrentDictionary<string, string> _statusMessages = new(StringComparer.OrdinalIgnoreCase);

        // The stream currently being processed by the single engine loop. LogStatus uses
        // this to attribute status messages to the right stream without threading the
        // identifier through every call site.
        private TorrentStreamItem? _currentStream;

        // Cache of file indices already moved to the destination, keyed by torrent identifier.
        // Once a file is confirmed moved (exists at destination with correct size), we stop
        // re-statting it on every poll cycle — this is the dominant per-cycle disk cost for
        // large torrents (thousands of files). A file is added here when first detected as
        // moved, or when we successfully copy it.
        private readonly ConcurrentDictionary<string, HashSet<int>> _movedFileCache = new(StringComparer.OrdinalIgnoreCase);

        // Consecutive connection failures per server profile. Reset when a server succeeds
        // or when a new engine instance is created (i.e. spool is restarted).
        private readonly ConcurrentDictionary<string, int> _serverFailures = new(StringComparer.OrdinalIgnoreCase);

        // Consecutive DRAIN passes where no file copied, keyed by torrent identifier. After
        // a threshold we force a delete+readd so qBittorrent's stale 100% progress (caused by
        // a mid-stream settings/cap change) is resynced with the actual on-disk data.
        private readonly ConcurrentDictionary<string, int> _drainFailures = new(StringComparer.OrdinalIgnoreCase);

        // Hard cap on consecutive DRAIN passes that make no progress. Beyond the delete+readd
        // resync (see _drainFailures), if the source files still can't be found we would retry
        // forever (e.g. qBittorrent configured with an "incomplete" folder that SpoolDatTorrent
        // cannot see because the volume isn't mounted). At this threshold the stream is flagged
        // Error with an actionable message instead of looping indefinitely.
        private const int MaxDrainFailuresBeforeError = 6;

        // Whether errored streams have already been re-activated for this engine instance.
        // A fresh engine = a restart, so errored streams are retried once at startup.
        private bool _hasReactivatedOnStart;

        // Signature of the last batch allocated per torrent (file count + footprint). Used
        // to avoid logging "Allocated batch" on every poll cycle when nothing changed.
        private readonly ConcurrentDictionary<string, string> _lastAllocatedBatch = new(StringComparer.OrdinalIgnoreCase);

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

            // On the first evaluation of this engine instance (i.e. a fresh spool run),
            // re-activate any errored streams so a fixed server is retried after a restart.
            if (!_hasReactivatedOnStart)
            {
                _hasReactivatedOnStart = true;
                foreach (var s in allStreams.Where(s => s.Status == StreamLifecycleStatus.Error))
                {
                    s.Status = StreamLifecycleStatus.Active;
                }
                await dbContext.SaveChangesAsync(cancellationToken);
            }

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
                    Logger.LogWarning($"Skipping unknown server profile '{profileName}'.");
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

                    // Process each stream on this server.
                    foreach (var stream in streamsOnThisServer)
                    {
                        await ProcessStreamAsync(stream, capPerStream, profileName, profileSettings, torrentClient, dbContext, cancellationToken);
                    }

                    // The entire server group (auth + all streams) succeeded: reset the
                    // failure counter. Note: this must be AFTER processing the streams, not
                    // after auth, otherwise a server with a bad API key (which only fails on
                    // a later call) would reset to 0 every cycle and never reach the limit.
                    _serverFailures[profileName] = 0;
                }
                catch (HttpRequestException ex)
                {
                    await HandleServerFailureAsync(dbContext, streamsOnThisServer, profileName, $"unreachable: {ex.Message}", cancellationToken);
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    await HandleServerFailureAsync(dbContext, streamsOnThisServer, profileName, "timed out", cancellationToken);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"BitTorrent client '{profileName}' failed: {ex.Message}");
                    await HandleServerFailureAsync(dbContext, streamsOnThisServer, profileName, $"error: {ex.Message}", cancellationToken);
                }
            }

            // 4. Report the full stream list (all jobs, not just the processed ones) so a
            //    dashboard can render every job with its latest known progress.
            await ReportAllStreamsAsync(dbContext, allStreams, cancellationToken);
        }

        private void LogStatus(string message)
        {
            // Status messages are per-cycle and would spam the tidy standard log, so they
            // are written at debug level (available for troubleshooting) while the UI still
            // shows them live via the reporter.
            Logger.LogDebug(message);

            // Attribute the message to the currently-processed stream (if any) so a
            // dashboard can show what a given stream is doing right now. Also push it into
            // the live snapshot immediately so the UI shows the current message rather than
            // the one captured at the start of the cycle.
            if (_currentStream != null)
            {
                _statusMessages[_currentStream.TorrentIdentifier] = message;
                if (_progressSnapshots.TryGetValue(_currentStream.TorrentIdentifier, out var liveSnapshot))
                {
                    liveSnapshot.StatusMessage = message;
                }
            }

            if (_progressReporter != null)
            {
                // Route through the reporter so a live display can render it cleanly.
                // Writing to the raw console here would corrupt Spectre's Live output.
                _progressReporter.ReportStatus(message);
            }
        }

        private string GetStreamStatusMessage(string torrentIdentifier)
        {
            return _statusMessages.TryGetValue(torrentIdentifier, out var msg) ? msg : string.Empty;
        }

        private void ReportStreamSnapshot(StreamProgressInfo snapshot)
        {
            _progressSnapshots[snapshot.TorrentIdentifier] = snapshot;
        }

        private async Task ReportAllStreamsAsync(SpoolDbContext dbContext, IReadOnlyList<TorrentStreamItem> allStreams, CancellationToken cancellationToken)
        {
            if (_progressReporter == null) return;

            // Re-read fresh statuses from the DB so a just-clicked Pause/Resume (which writes
            // via a different DbContext) is reflected immediately, rather than being
            // overwritten by the stale in-memory list loaded at the start of this cycle.
            var freshStatuses = await dbContext.Streams
                .AsNoTracking()
                .Select(s => new { s.TorrentIdentifier, s.Status, s.MovedCount, s.TotalCount })
                .ToDictionaryAsync(s => s.TorrentIdentifier, cancellationToken);

            var list = allStreams
                .Select(s =>
                {
                    if (_progressSnapshots.TryGetValue(s.TorrentIdentifier, out var snap))
                    {
                        if (freshStatuses.TryGetValue(s.TorrentIdentifier, out var fresh))
                        {
                            snap.Status = fresh.Status.ToString();
                            snap.MovedCount = fresh.MovedCount;
                            snap.TotalCount = fresh.TotalCount;
                        }
                        return snap;
                    }

                    return new StreamProgressInfo
                    {
                        Name = s.Name,
                        TorrentIdentifier = s.TorrentIdentifier,
                        StreamId = s.Id,
                        Status = freshStatuses.TryGetValue(s.TorrentIdentifier, out var f) ? f.Status.ToString() : s.Status.ToString()
                    };
                })
                .ToList();

            _progressReporter.ReportStreams(list);
        }

        private async Task HandleServerFailureAsync(
            SpoolDbContext dbContext,
            List<TorrentStreamItem> streams,
            string profileName,
            string reason,
            CancellationToken cancellationToken)
        {
            int failures = _serverFailures.AddOrUpdate(profileName, 1, (_, count) => count + 1);
            int retryCount = _settings.ServerRetryCount < 0 ? 0 : _settings.ServerRetryCount;

            if (failures <= retryCount)
            {
                // Retry later — leave the streams Active so they keep being polled.
                LogStatus($"BitTorrent client '{profileName}' {reason}. Retry {failures}/{retryCount}.");
                return;
            }

            // Retries exhausted: mark the streams as Error so polling stops.
            LogStatus($"BitTorrent client '{profileName}' {reason}. Retries exhausted ({failures}); marking streams as errored.");
            await MarkServerStreamsErroredAsync(dbContext, streams, profileName, cancellationToken);
        }

        private async Task MarkServerStreamsErroredAsync(
            SpoolDbContext dbContext,
            List<TorrentStreamItem> streams,
            string profileName,
            CancellationToken cancellationToken)
        {
            bool changed = false;
            foreach (var stream in streams)
            {
                if (stream.Status != StreamLifecycleStatus.Error)
                {
                    stream.Status = StreamLifecycleStatus.Error;
                    changed = true;
                    Logger.LogError($"Marked stream '{stream.Name}' as Error because server '{profileName}' is unreachable.");
                }
            }

            if (changed)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        private HashSet<int> GetMovedFileCache(string torrentIdentifier)
        {
            return _movedFileCache.GetOrAdd(torrentIdentifier, _ => new HashSet<int>());
        }

        private bool IsAlreadyMoved(string torrentIdentifier, TorrentFileDto file, string destinationRoot, string prefixToStrip)
        {
            var cache = GetMovedFileCache(torrentIdentifier);

            string destPath = GetDestinationPath(destinationRoot, prefixToStrip, file.Name);

            // Fast path: the cache says this file was moved, but only trust it if the
            // destination file still exists on disk. If it's been deleted (e.g. the user
            // cleared the destination to re-download), drop it from the cache and treat it
            // as not moved so it gets re-allocated.
            if (cache.Contains(file.Index))
            {
                if (File.Exists(destPath) && new FileInfo(destPath).Length == file.Size)
                {
                    return true;
                }

                cache.Remove(file.Index);
            }

            // Slow path: stat the destination to confirm presence + size.
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

        /// <summary>
        /// Resolve the actual on-disk source path for a file, tolerating the race where
        /// qBittorrent moves a file from the incomplete folder (content_path) to the
        /// completed folder (save_path) between our poll and our copy. Checks both candidate
        /// locations and returns the first that exists with the expected size.
        /// </summary>
        private string ResolveSourcePath(
            string torrentContentPath,
            string torrentSavePath,
            string fileRelativeName,
            long expectedSize,
            TorrentServerProfile profile)
        {
            var candidates = new[]
            {
                TranslateToLocalPath(torrentContentPath, fileRelativeName, profile),
                TranslateToLocalPath(torrentSavePath, fileRelativeName, profile)
            };

            foreach (var candidate in candidates)
            {
                try
                {
                    var info = new FileInfo(candidate);
                    if (info.Exists && info.Length == expectedSize)
                    {
                        return candidate;
                    }
                }
                catch (IOException)
                {
                    // Skip invalid paths.
                }
            }

            // None found — return the primary (content) candidate so the caller's error
            // message and "Actual: N" reflects the most likely location.
            return candidates[0];
        }

        // Used by Web/Docker: Runs continuously in the background
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Logger.Log("🔄 Spooling engine started. Polling every " + _settings.PollIntervalSeconds + "s.");
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await EvaluateAllStreamsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"SpoolingEngine encountered an error: {ex.Message}");
                }

                await Task.Delay(TimeSpan.FromSeconds(_settings.PollIntervalSeconds), stoppingToken);
            }

            // Log which streams were still active so the operator can see what was in flight
            // when the container stopped. Streams are left Active (stateless-by-design) so
            // they resume automatically on restart.
            await LogActiveStreamsOnShutdownAsync(stoppingToken);
            Logger.Log("🛑 Spooling engine stopped.");
        }

        public async Task LogActiveStreamsOnShutdownAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<SpoolDbContext>();
                var active = await dbContext.Streams
                    .Where(s => s.Status == StreamLifecycleStatus.Active)
                    .OrderBy(s => s.Id)
                    .ToListAsync(cancellationToken);

                if (active.Count == 0)
                {
                    Logger.Log("ℹ️ No active streams at shutdown.");
                    return;
                }

                var summary = active.Select(s => $"#{s.Id} '{s.Name}' ({s.MovedCount}/{s.TotalCount} moved)");
                Logger.Log($"ℹ️ {active.Count} active stream(s) at shutdown (will resume on restart): {string.Join("; ", summary)}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Could not list active streams at shutdown: {ex.Message}");
            }
        }

        private string GetActiveDatPath(TorrentStreamItem stream)
        {
            // Prefer the cached .dat copy (robust to the original being deleted), falling
            // back to the original path.
            return !string.IsNullOrWhiteSpace(stream.CachedDatPath)
                ? stream.CachedDatPath
                : stream.DatFilePath;
        }

        private async Task ProcessStreamAsync(
                            TorrentStreamItem stream,
                            long allocatedCapBytes,
                            string serverProfileName,
                            TorrentServerProfile profileSettings,
                            IBitTorrentClient torrentClient,
                            SpoolDbContext dbContext,
                            CancellationToken cancellationToken)
        {
            _currentStream = stream;
            Logger.LogDebug($"Processing stream '{stream.Name}' (id {stream.Id}) on server '{serverProfileName}': strategy={stream.Strategy}, cap={allocatedCapBytes.ToGigabytes():0.#} GB, priorityTerms='{stream.PriorityTerms}', dePriorityTerms='{stream.DePriorityTerms}'.");
            var desiredGames = await GetDesiredGamesAsync(GetActiveDatPath(stream), cancellationToken);

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
            string torrentContentPath = await torrentClient.GetTorrentContentPathAsync(stream.TorrentIdentifier, cancellationToken);
            string torrentName = await torrentClient.GetTorrentNameAsync(stream.TorrentIdentifier, cancellationToken);
            var torrentFiles = await torrentClient.GetFilesAsync(stream.TorrentIdentifier, cancellationToken);

            if (torrentFiles == null || !torrentFiles.Any()) return;

            // Fetch the client-reported torrent info (size, downloaded, state) so the UI can
            // show an accurate download progress bar and what qBittorrent is doing.
            var torrentInfo = await torrentClient.GetTorrentInfoAsync(stream.TorrentIdentifier, cancellationToken);

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
                else
                {
                    // Not yet on disk, not currently downloading. This includes files with
                    // priority 0 (e.g. demoted after being moved, or skipped by the cap).
                    // Treat them as pending so they get re-allocated — otherwise a resumed
                    // stream whose destination files were deleted would be seen as complete
                    // immediately (nothing in downloading/ready/pending). AllocateBatchAsync
                    // re-applies the cap, so re-selecting them is correct.
                    pending.Add(file);
                }
            }

            // Emit a progress snapshot for this stream: overall job progress (moved vs
            // total desired) plus the current batch's files and their download progress.
            var snapshot = new StreamProgressInfo
            {
                Name = stream.Name,
                TorrentIdentifier = stream.TorrentIdentifier,
                StreamId = stream.Id,
                Status = stream.Status.ToString(),
                TorrentName = torrentName,
                ServerName = serverProfileName,
                CreatedUtc = stream.CreatedUtc,
                TotalSizeBytes = desiredFiles.Sum(f => f.Size),
                AllocatedCapBytes = allocatedCapBytes,
                StatusMessage = GetStreamStatusMessage(stream.TorrentIdentifier),
                ClientSizeBytes = torrentInfo?.Size ?? 0,
                ClientDownloadedBytes = torrentInfo?.Downloaded ?? 0,
                ClientState = torrentInfo?.State ?? string.Empty,
                ClientSeeds = torrentInfo?.NumSeeds ?? 0,
                ClientSeedsTotal = torrentInfo?.NumComplete ?? 0,
                ClientPeers = torrentInfo?.NumPeers ?? 0,
                ClientPeersTotal = torrentInfo?.NumIncomplete ?? 0,
                ClientDownSpeed = torrentInfo?.Dlspeed ?? 0,
                ClientEta = torrentInfo?.Eta ?? -1,
                MovedCount = alreadyMoved.Count,
                TotalCount = desiredFiles.Count,
                Files = downloading
                    .Concat(readyToMove)
                    .Select(f => new FileProgressInfo
                    {
                        Name = Path.GetFileName(f.Name),
                        Progress = f.Progress,
                        StreamId = stream.Id,
                        SizeBytes = f.Size,
                        Status = f.Progress >= 1.0f ? "Downloaded" : "Downloading"
                    })
                    .ToList()
            };
            ReportStreamSnapshot(snapshot);

            // Persist progress to the DB so it survives app restarts and is queryable by
            // the list command / web UI even when the engine is not running.
            stream.MovedCount = alreadyMoved.Count;
            stream.TotalCount = desiredFiles.Count;

            // STATE: WAIT — files are actively downloading. Do nothing until the whole
            // batch completes, so we never delete the torrent mid-download.
            LogStatus($"Awaiting completion of current download batch...");
            if (downloading.Any())
            {
                var inProgress = downloading.Select(f => $"{f.Name} ({f.Progress:P})");
                Logger.LogDebug($"[Spooling] Batch active. Waiting for {downloading.Count} files: {string.Join(", ", inProgress)}");
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

                // Wait for the client to finish writing/flushing the completed files before
                // copying them. Uses the stream's per-stream settling time, falling back to
                // the global default.
                int settleSeconds = stream.SettlingTimeSeconds ?? _settings.SettlingTimeSeconds;
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, settleSeconds)), cancellationToken);

                var copiedIndices = new List<int>();
                bool corruptSourceDetected = false;

                foreach (var file in readyToMove)
                {
                    string destinationPath = GetDestinationPath(destinationRoot, prefixToStrip, file.Name);
                    string sourcePath = ResolveSourcePath(torrentContentPath, torrentSavePath, file.Name, file.Size, profileSettings);

                    LogStatus($"Moving file: {Path.GetFileName(file.Name)}...");
                    Logger.LogDebug($"[DRAIN] source='{sourcePath}' dest='{destinationPath}' expected={file.Size}");

                    try
                    {
                        await CopyAndVerifyAsync(sourcePath, destinationPath, file.Size, cancellationToken);
                        copiedIndices.Add(file.Index);
                        MarkMoved(stream.TorrentIdentifier, file.Index);
                    }
                    catch (IOException ex)
                    {
                        // Distinguish a genuinely-missing/0-byte source (qBittorrent's stale
                        // 100% progress) from a transient copy failure. The former is the
                        // infinite-loop trigger and needs a delete+readd resync.
                        if (ex.Message.Contains("not fully formed", StringComparison.OrdinalIgnoreCase))
                        {
                            corruptSourceDetected = true;
                        }

                        LogStatus($"Move failed for '{Path.GetFileName(file.Name)}' (will retry next loop): {ex.Message} [source: {sourcePath}]");
                        Logger.LogDebug($"[DRAIN] move failed: {ex.Message}");
                    }
                }

                if (copiedIndices.Any())
                {
                    // Progress was made — reset the drain-failure counter.
                    _drainFailures.TryRemove(stream.TorrentIdentifier, out _);
                    Logger.Log($"🚛 Moved {copiedIndices.Count} file(s) for stream '{stream.Name}'. Rebuilding torrent for next batch... Files: {FormatFileCsv(torrentFiles, copiedIndices)}");
                    LogStatus($"Moved {copiedIndices.Count} files. Rebuilding torrent for next batch...");
                    await RebuildTorrentForNextBatchAsync(stream, torrentSavePath, torrentName, torrentFiles, profileSettings, desiredGames, allocatedCapBytes, torrentClient, cancellationToken);
                }
                else if (corruptSourceDetected)
                {
                    // qBittorrent reports files as 100% but the on-disk data is gone (likely
                    // a delete+readd during a mid-stream settings change). Force a rebuild so
                    // qBittorrent re-checks and re-downloads, instead of looping forever.
                    int failures = _drainFailures.AddOrUpdate(stream.TorrentIdentifier, 1, (_, c) => c + 1);
                    Logger.LogDebug($"[DRAIN] corrupt source detected, consecutive failures={failures}");

                    if (failures >= MaxDrainFailuresBeforeError)
                    {
                        // Repeatedly resyncing has not helped — the completed files still
                        // can't be found on disk. The most likely cause is a qBittorrent
                        // "keep incomplete torrents in" folder that SpoolDatTorrent can't
                        // see (the volume isn't mounted at the same path). Stop retrying
                        // and surface an actionable Error instead of looping forever.
                        _drainFailures.TryRemove(stream.TorrentIdentifier, out _);
                        string message =
                            "Completed files are reported as done but cannot be found on disk. " +
                            "If qBittorrent uses a separate 'incomplete' folder, mount it at the " +
                            "same path in SpoolDatTorrent. See logs for details.";
                        _statusMessages[stream.TorrentIdentifier] = message;
                        LogStatus("Move keeps failing to locate completed files; marking stream as Error.");
                        stream.Status = StreamLifecycleStatus.Error;
                        await torrentClient.ResumeTorrentAsync(stream.TorrentIdentifier, cancellationToken);
                    }
                    else if (failures >= 2)
                    {
                        _drainFailures.TryRemove(stream.TorrentIdentifier, out _);
                        LogStatus($"Source data missing; forcing a re-check to resync with qBittorrent...");
                        await RebuildTorrentForNextBatchAsync(stream, torrentSavePath, torrentName, torrentFiles, profileSettings, desiredGames, allocatedCapBytes, torrentClient, cancellationToken);
                    }
                    else
                    {
                        // First occurrence — give qBittorrent time to flush the file to disk
                        // before retrying. A 100%-reported file can still be 0 bytes on disk
                        // briefly (write-buffer flush), so hammering it every second is noisy
                        // and pointless. Wait the settling time, then resume and retry next
                        // cycle. The delete+readd safeguard (>= 2 consecutive failures) is
                        // unchanged and still handles genuinely-stale progress.
                        int backoffSeconds = stream.SettlingTimeSeconds ?? _settings.SettlingTimeSeconds;
                        await Task.Delay(TimeSpan.FromSeconds(Math.Max(2, backoffSeconds)), cancellationToken);
                        await torrentClient.ResumeTorrentAsync(stream.TorrentIdentifier, cancellationToken);
                    }
                }
                else
                {
                    // Transient copy failure (e.g. destination locked) — resume and retry.
                    _drainFailures.TryRemove(stream.TorrentIdentifier, out _);
                    await torrentClient.ResumeTorrentAsync(stream.TorrentIdentifier, cancellationToken);
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            // STATE: ALLOCATE — nothing downloading and nothing completed. Either first run
            // (torrent added paused) or a freshly re-added torrent. Set priorities and resume.
            if (pending.Any())
            {
                LogStatus($"Constructing next batch to fit into maximum spool size ({allocatedCapBytes.ToGigabytes():0.#} GB)...");
                await AllocateBatchAsync(stream, torrentFiles, desiredGames, alreadyMoved, allocatedCapBytes, torrentClient, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            // STATE: COMPLETE — nothing left to download or move. Verify the destination
            // before declaring the stream complete: every desired file should exist at its
            // destination with the correct size. If any are missing/wrong-sized, flag the
            // stream as Error instead of falsely reporting completion (no auto-fix).
            var (verified, missing) = VerifyDestination(
                destinationRoot, prefixToStrip, desiredFiles, alreadyMoved, profileSettings);

            if (!verified)
            {
                Logger.LogError($"Completion verification failed for stream '{stream.Name}': {missing.Count} file(s) missing or wrong size at destination.");
                LogStatus($"Completion verification failed: {missing.Count} file(s) missing or wrong size at destination.");
                foreach (var name in missing.Take(10))
                {
                    Logger.LogDebug($"[Verify] {name}");
                }
                stream.Status = StreamLifecycleStatus.Error;
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            Logger.Log($"✅ Spooling complete for stream '{stream.Name}' ({alreadyMoved.Count} files).");
            LogStatus($"All files verified and in destination folder. Stream ({stream.Id}) '{stream.Name}' completed!");
            stream.Status = StreamLifecycleStatus.Completed;
            await dbContext.SaveChangesAsync(cancellationToken);

            // Re-emit the snapshot with the final Completed status and the verification
            // message, so the UI shows 100% + the message and it persists across polls
            // (the snapshot built at the start of this method still had Status=Active).
            snapshot.Status = StreamLifecycleStatus.Completed.ToString();
            snapshot.StatusMessage = GetStreamStatusMessage(stream.TorrentIdentifier);
            snapshot.MovedCount = alreadyMoved.Count;
            snapshot.TotalCount = desiredFiles.Count;
            ReportStreamSnapshot(snapshot);
        }

        /// <summary>
        /// Verify that every desired file that should have been moved now exists at its
        /// destination with the correct size. Returns (success, list of problem file names).
        /// </summary>
        private (bool Success, List<string> Problems) VerifyDestination(
            string destinationRoot,
            string prefixToStrip,
            List<TorrentFileDto> desiredFiles,
            List<TorrentFileDto> alreadyMoved,
            TorrentServerProfile profileSettings)
        {
            var problems = new List<string>();

            foreach (var file in desiredFiles)
            {
                // Files not yet moved (still in the torrent, not spooled) are not part of
                // this verification — they belong to the not-yet-processed remainder.
                if (!alreadyMoved.Any(f => f.Index == file.Index))
                {
                    continue;
                }

                string destinationPath = GetDestinationPath(destinationRoot, prefixToStrip, file.Name);
                var info = new FileInfo(destinationPath);

                if (!info.Exists || info.Length != file.Size)
                {
                    problems.Add($"{file.Name} (expected {file.Size}, actual {(info.Exists ? info.Length : 0)})");
                }
            }

            return (problems.Count == 0, problems);
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

        /// <summary>
        /// Rank a file's download priority based on the stream's comma-separated terms.
        /// 0 = matches a priority term (download first), 2 = matches a de-priority term
        /// (download last), 1 = neutral. Substring match, case-insensitive.
        /// </summary>
        private static int GetPriorityRank(string fileName, string priorityTerms, string dePriorityTerms)
        {
            if (MatchesAnyTerm(fileName, priorityTerms))
            {
                return 0;
            }

            if (MatchesAnyTerm(fileName, dePriorityTerms))
            {
                return 2;
            }

            return 1;
        }

        private static bool MatchesAnyTerm(string fileName, string csvTerms)
        {
            if (string.IsNullOrWhiteSpace(csvTerms))
            {
                return false;
            }

            var name = fileName;
            foreach (var raw in csvTerms.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrWhiteSpace(raw) &&
                    name.Contains(raw, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Build a comma-separated list of file names (basename only) for the given indices,
        /// used to annotate "Moved"/"Allocated" log lines with the actual files involved.
        /// </summary>
        private static string FormatFileCsv(IReadOnlyList<TorrentFileDto> files, IEnumerable<int> indices)
        {
            var indexSet = new HashSet<int>(indices);
            var names = files
                .Where(f => indexSet.Contains(f.Index))
                .Select(f => Path.GetFileName(f.Name))
                .ToList();

            return names.Count == 0 ? "(none)" : string.Join(", ", names);
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

            // Order the whole desired set once by the stream's priority/de-priority terms.
            // High-priority files come first, de-priority files last, so batches fill the
            // high-priority ROMs first across the entire run (not just within one batch).
            var orderedFiles = torrentFiles
                .OrderBy(f => GetPriorityRank(f.Name, stream.PriorityTerms, stream.DePriorityTerms))
                .ThenBy(f => f.Index)
                .ToList();

            // Determine which files are candidates for this batch (DAT-matched, not yet
            // moved, not already selected/skipped).
            var candidates = new List<(TorrentFileDto File, int Rank)>();
            foreach (var file in orderedFiles)
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

                candidates.Add((file, GetPriorityRank(file.Name, stream.PriorityTerms, stream.DePriorityTerms)));
            }

            // Build the batch from two pools:
            //  - High-priority pool: ranks 0 (priority) + 1 (neutral), mixed together
            //    (priority files come first, neutral tops up the batch).
            //  - De-priority pool: rank 2 (de-priority) only.
            // Fill the high-priority pool first; de-priority files are only downloaded once
            // no high-priority file remains. De-priority never mixes with priority.
            var orderedCandidates = candidates.OrderBy(c => c.Rank).ToList();
            var highPriorityPool = orderedCandidates.Where(c => c.Rank <= 1).Select(c => c.File).ToList();
            var dePriorityPool = orderedCandidates.Where(c => c.Rank == 2).Select(c => c.File).ToList();

            List<TorrentFileDto> selectedPool;
            if (highPriorityPool.Any(f => f.Size <= allocatedCapBytes - currentFootprint))
            {
                selectedPool = highPriorityPool;
            }
            else if (dePriorityPool.Any(f => f.Size <= allocatedCapBytes - currentFootprint))
            {
                selectedPool = dePriorityPool;
            }
            else
            {
                // No file fits the remaining cap; fall back to the high-priority pool so
                // its files are at least marked (they'll be skipped below if oversized).
                selectedPool = highPriorityPool;
            }

            // Fill the batch from the selected pool, respecting the cap.
            foreach (var file in selectedPool)
            {
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

            // CRITICAL: every candidate NOT selected for download must be skipped (priority 0).
            // Otherwise lower-tier files (e.g. Japan) keep their previous priority and
            // download alongside, which balloons the batch beyond the cap.
            foreach (var (file, _) in candidates)
            {
                if (!filesToDownload.Contains(file.Index))
                {
                    filesToSkip.Add(file.Index);
                }
            }

            if (filesToSkip.Any()) await torrentClient.SetFilePrioritiesAsync(stream.TorrentIdentifier, filesToSkip, 0, cancellationToken);
            if (filesToDownload.Any()) await torrentClient.SetFilePrioritiesAsync(stream.TorrentIdentifier, filesToDownload, 1, cancellationToken);

            // Only log when the batch actually changes. The engine re-runs allocation every
            // poll cycle (re-applying priorities), so logging unconditionally would spam the
            // standard log with identical "Allocated batch" lines.
            string signature = $"{filesToDownload.Count}|{currentFootprint}";
            if (_lastAllocatedBatch.TryGetValue(stream.TorrentIdentifier, out var previous) && previous == signature)
            {
                await torrentClient.ResumeTorrentAsync(stream.TorrentIdentifier, cancellationToken);
                return;
            }

            _lastAllocatedBatch[stream.TorrentIdentifier] = signature;
            Logger.Log($"📦 Allocated batch of {filesToDownload.Count} file(s) ({currentFootprint.ToGigabytes():0.#} GB) for stream '{stream.Name}'. Resuming download... Files: {FormatFileCsv(torrentFiles, filesToDownload)}");
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
            //    Prefer the cached .torrent copy (which is robust to the original being
            //    deleted), falling back to the original path or magnet.
            string? source = !string.IsNullOrWhiteSpace(stream.CachedTorrentPath)
                ? stream.CachedTorrentPath
                : !string.IsNullOrWhiteSpace(stream.OriginalTorrentPath)
                    ? stream.OriginalTorrentPath
                    : stream.OriginalMagnet;

            if (string.IsNullOrWhiteSpace(source))
            {
                LogStatus($"Cannot rebuild torrent: no .torrent source (cached/original/magnet) stored for stream '{stream.Name}'.");
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
            // Prefer the cached .torrent copy, falling back to the original path or magnet.
            string? source = !string.IsNullOrWhiteSpace(stream.CachedTorrentPath)
                ? stream.CachedTorrentPath
                : !string.IsNullOrWhiteSpace(stream.OriginalTorrentPath)
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
