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
using System.Collections.Generic;
using System.Linq;
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

        public SpoolingEngine(
            IServiceScopeFactory scopeFactory,
            IBitTorrentClientFactory clientFactory,
            IDatParserService datParser,
            IOptions<GlobalSpoolSettings> settings)
        {
            _scopeFactory = scopeFactory;
            _clientFactory = clientFactory;
            _datParser = datParser;
            _settings = settings.Value;
        }

        // Used by future Desktop App: Can be called on-demand by a UI button or on app startup
        public async Task EvaluateAllStreamsAsync(CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<SpoolDbContext>();

            // 1. Fetch all Active streams from the database, including their tracked files
            var activeStreams = await dbContext.Streams
                .Include(s => s.Files)
                .Where(s => s.Status == StreamLifecycleStatus.Active)
                .ToListAsync(cancellationToken);

            if (!activeStreams.Any())
            {
                return; // Nothing to do, go back to sleep
            }

            // 2. Group the streams by their designated Server Profile
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

                // Get the authenticated client for this specific server
                var torrentClient = _clientFactory.GetClient(profileName);
                await torrentClient.AuthenticateAsync(cancellationToken);

                // Process each stream on this server
                foreach (var stream in streamsOnThisServer)
                {
                    await ProcessStreamAsync(stream, capPerStream, profileSettings, torrentClient, dbContext, cancellationToken);
                }
            }
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
            var desiredGames = await _datParser.GetGameNamesFromFileAsync(stream.DatFilePath, cancellationToken);
            string torrentSavePath = await torrentClient.GetTorrentSavePathAsync(stream.TorrentIdentifier, cancellationToken);
            string torrentName = await torrentClient.GetTorrentNameAsync(stream.TorrentIdentifier, cancellationToken);
            var torrentFiles = await torrentClient.GetFilesAsync(stream.TorrentIdentifier, cancellationToken);

            if (torrentFiles == null || !torrentFiles.Any()) return;

            // Destination root: [base]/[stream name]/[torrent name] to avoid filename
            // collisions across servers/torrents. Relative subfolder structure within the
            // torrent is preserved to avoid intra-torrent collisions.
            string baseRoot = string.IsNullOrWhiteSpace(stream.SpoolingTargetOverride)
                ? _settings.DefaultSpoolingTarget
                : stream.SpoolingTargetOverride;
            string destinationRoot = Path.Combine(
                baseRoot,
                SanitizeFolderName(stream.Name),
                SanitizeFolderName(string.IsNullOrWhiteSpace(torrentName) ? stream.TorrentIdentifier : torrentName));

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
                string destPath = GetDestinationPath(destinationRoot, file.Name);
                bool isSpooled = File.Exists(destPath) && new FileInfo(destPath).Length == file.Size;

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

            // STATE: WAIT — files are actively downloading. Do nothing until the whole
            // batch completes, so we never delete the torrent mid-download.
            if (downloading.Any())
            {
                var inProgress = downloading.Select(f => $"{Path.GetFileName(f.Name)} ({f.Progress:P})");
                Logger.Log($"[Spooling] Batch active. Waiting for {downloading.Count} files: {string.Join(", ", inProgress)}", echoToConsole: false);
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
                Logger.Log($"[Spooling] Halting torrent to copy {readyToMove.Count} completed files...", echoToConsole: true);
                await torrentClient.PauseTorrentAsync(stream.TorrentIdentifier, cancellationToken);
                await Task.Delay(3000, cancellationToken);

                var copiedIndices = new List<int>();

                foreach (var file in readyToMove)
                {
                    string destinationPath = GetDestinationPath(destinationRoot, file.Name);
                    string sourcePath = TranslateToLocalPath(torrentSavePath, file.Name, profileSettings);

                    Logger.Log($"[Spooling] Attempting copy: {file.Name}...", echoToConsole: true);

                    try
                    {
                        await CopyAndVerifyAsync(sourcePath, destinationPath, file.Size, cancellationToken);
                        copiedIndices.Add(file.Index);
                    }
                    catch (IOException ex)
                    {
                        Logger.Log($"[Error] Copy failed (will retry next loop): {ex.Message}", echoToConsole: true);
                    }
                }

                if (copiedIndices.Any())
                {
                    Logger.Log($"[Spooling] Copied {copiedIndices.Count} files. Rebuilding torrent for next batch...", echoToConsole: true);
                    await RebuildTorrentForNextBatchAsync(stream, torrentSavePath, torrentName, desiredGames, allocatedCapBytes, torrentClient, cancellationToken);
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
                Logger.Log($"[Spooling] Allocating next batch up to storage cap...", echoToConsole: true);
                await AllocateBatchAsync(stream, torrentFiles, desiredGames, alreadyMoved, allocatedCapBytes, torrentClient, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            // STATE: COMPLETE — nothing left to download or move
            Logger.Log($"[Spooling] Stream entirely completed!", echoToConsole: true);
            stream.Status = StreamLifecycleStatus.Completed;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        private static string SanitizeFolderName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
            return string.IsNullOrWhiteSpace(sanitized) ? "stream" : sanitized;
        }

        private static string GetDestinationPath(string destinationRoot, string fileRelativeName)
        {
            // Preserve the torrent's internal subfolder structure to avoid filename collisions
            return Path.Combine(destinationRoot, fileRelativeName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
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
            HashSet<string> desiredGames,
            long allocatedCapBytes,
            IBitTorrentClient torrentClient,
            CancellationToken cancellationToken)
        {
            // 1. Delete the whole torrent AND its downloaded data. This is safe because we
            //    have already copied every completed file to its final destination.
            //    (DeleteTorrentAsync polls internally until the torrent is actually gone.)
            await torrentClient.DeleteTorrentAsync(stream.TorrentIdentifier, deleteFiles: true, cancellationToken);

            // 2. Re-add the SAME torrent source (same info-hash => same swarm), paused.
            string? source = !string.IsNullOrWhiteSpace(stream.OriginalTorrentPath)
                ? stream.OriginalTorrentPath
                : stream.OriginalMagnet;

            if (string.IsNullOrWhiteSpace(source))
            {
                Logger.Log($"[Error] Cannot rebuild torrent: no original .torrent path or magnet stored for stream '{stream.Name}'.", echoToConsole: true);
                return;
            }

            await torrentClient.AddTorrentAsync(source, torrentSavePath, addPaused: true, cancellationToken);

            // 3. Wait for qBittorrent to parse the file tree before setting priorities
            await Task.Delay(5000, cancellationToken);

            // 4. Re-fetch the fresh file list and allocate the next batch
            var freshFiles = await torrentClient.GetFilesAsync(stream.TorrentIdentifier, cancellationToken);
            if (freshFiles == null || !freshFiles.Any()) return;

            var alreadyMoved = new List<TorrentFileDto>();
            string baseRoot = string.IsNullOrWhiteSpace(stream.SpoolingTargetOverride)
                ? _settings.DefaultSpoolingTarget
                : stream.SpoolingTargetOverride;
            string destinationRoot = Path.Combine(
                baseRoot,
                SanitizeFolderName(stream.Name),
                SanitizeFolderName(string.IsNullOrWhiteSpace(torrentName) ? stream.TorrentIdentifier : torrentName));

            foreach (var file in freshFiles)
            {
                string destPath = GetDestinationPath(destinationRoot, file.Name);
                if (File.Exists(destPath) && new FileInfo(destPath).Length == file.Size)
                {
                    alreadyMoved.Add(file);
                }
            }

            await AllocateBatchAsync(stream, freshFiles, desiredGames, alreadyMoved, allocatedCapBytes, torrentClient, cancellationToken);
        }
    }
}
