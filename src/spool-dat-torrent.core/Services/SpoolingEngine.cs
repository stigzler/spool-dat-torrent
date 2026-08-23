using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SpoolDatTorrent.Core.Configuration;
using SpoolDatTorrent.Core.Data;
using SpoolDatTorrent.Core.Interfaces;
using SpoolDatTorrent.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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

        private async Task CopyAndVerifyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
        {
            // FileShare.ReadWrite is the magic key that bypasses the qBittorrent file lock
            using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var destStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await sourceStream.CopyToAsync(destStream, 81920, cancellationToken);
            }

            // Verify the transfer was perfect
            var srcInfo = new FileInfo(sourcePath);
            var destInfo = new FileInfo(destinationPath);

            if (srcInfo.Length != destInfo.Length)
            {
                File.Delete(destinationPath); // Nuke the corrupted target
                throw new IOException($"Verification failed. Size mismatch. Source: {srcInfo.Length}, Dest: {destInfo.Length}");
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
            // 1. Get the list of desired games from the local DAT file
            var desiredGames = await _datParser.GetGameNamesFromFileAsync(stream.DatFilePath, cancellationToken);

            // 1.5 Fetch the physical root save path from the client
            string torrentSavePath = await torrentClient.GetTorrentSavePathAsync(stream.TorrentIdentifier, cancellationToken);

            // 2. Fetch the current files and their progress from the BT Client
            var torrentFiles = await torrentClient.GetFilesAsync(stream.TorrentIdentifier, cancellationToken);
            if (torrentFiles == null || !torrentFiles.Any())
            {
                Console.WriteLine($"No files found for torrent {stream.TorrentIdentifier}. It may not be loaded yet.");
                return;
            }

            // Resolve the final destination path, falling back to global settings if stream override is empty
            string? destinationRoot = string.IsNullOrWhiteSpace(stream.SpoolingTargetOverride)
                ? _settings.DefaultSpoolingTarget
                : stream.SpoolingTargetOverride;

            long currentSpoolFootprint = 0;
            var filesToDownload = new List<int>();
            var filesToSkip = new List<int>();

            // 3. Evaluate every file in the torrent against completion, DAT list, and Cap
            foreach (var file in torrentFiles)
            {
                var gameName = Path.GetFileNameWithoutExtension(file.Name);
                bool isDesired = desiredGames.Contains(gameName);

            // --- PASS 1: Check for completed files ready to be moved ---
                if (isDesired && file.Progress >= 1.0f && !string.IsNullOrEmpty(destinationRoot))
                {
                    try
                    {
                        // We use Path.GetFileName to strip away the massive Redump folder structure 
                        // and drop the zip file directly into your target folder.
                        string destinationPath = Path.Combine(destinationRoot, Path.GetFileName(file.Name));

                        // Use our shiny new translation layer to find the actual physical file!
                        string sourcePath = TranslateToLocalPath(torrentSavePath, file.Name, profileSettings);

                        if (File.Exists(sourcePath))
                        {
                            // Only copy if it isn't already sitting in the destination
                            if (!File.Exists(destinationPath) || new FileInfo(destinationPath).Length != file.Size)
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                                Console.WriteLine($"[Spooling] Copying file to Output: {Path.GetFileName(file.Name)}...");
                                
                                await CopyAndVerifyAsync(sourcePath, destinationPath, cancellationToken);
                                //Console.WriteLine($"[Spooling] Successfully moved to pool.");
                            }

                            // Tell Pass 3 to issue a Priority 0 command to qBittorrent to stop seeding it
                            filesToSkip.Add(file.Index);
                        }
                        else
                        {
                            // If it's 100% complete but the source file is missing, we already moved and deleted it successfully on a previous run.
                            filesToSkip.Add(file.Index);
                        }
                        
                        continue;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Error] Failed to process completed file {gameName}: {ex.Message}");
                    }
                }
                // --- PASS 2: Spool Cap Allocation for active/remaining files ---
                if (isDesired)
                {
                    if (file.Progress >= 1.0f || currentSpoolFootprint + file.Size <= allocatedCapBytes)
                    {
                        filesToDownload.Add(file.Index);

                        if (file.Progress < 1.0f)
                        {
                            currentSpoolFootprint += file.Size;
                        }
                    }
                    else
                    {
                        filesToSkip.Add(file.Index);
                    }
                }
                else
                {
                    filesToSkip.Add(file.Index);
                }
            }

            // 4. Dispatch final priority commands to qBittorrent
            if (filesToSkip.Any())
            {
                await torrentClient.SetFilePrioritiesAsync(stream.TorrentIdentifier, filesToSkip, 0, cancellationToken);
            }

            if (filesToDownload.Any())
            {
                await torrentClient.SetFilePrioritiesAsync(stream.TorrentIdentifier, filesToDownload, 1, cancellationToken);
            }

            // TODO: [Phase 3 - Cleanup & Completion]
            // If filesToDownload is empty AND all 'isDesired' files have been successfully copied to the destination pool:
            // 1. Issue an API command to qBittorrent to delete the torrent (with deleteFiles = true) to wipe the scratch drive and .parts files.
            // 2. Update this stream's Status in SQLite from StreamLifecycleStatus.Active to StreamLifecycleStatus.Completed.
            // 3. Delete files in complete folder if user settings indicate this.

            // 5. Sync state changes to SQLite
            await dbContext.SaveChangesAsync(cancellationToken);

            // 6. Release the brakes now that priorities are safely set
            await torrentClient.ResumeTorrentAsync(stream.TorrentIdentifier, cancellationToken);
        }
    }
}
