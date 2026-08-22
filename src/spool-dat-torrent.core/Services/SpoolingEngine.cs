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
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IBitTorrentClientFactory _clientFactory;
        private readonly IDatParserService _datParser;
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
                    await ProcessStreamAsync(stream, capPerStream, torrentClient, dbContext, cancellationToken);
                }
            }
        }

        private async Task ProcessStreamAsync(
                    TorrentStreamItem stream,
                    long allocatedCapBytes,
                    IBitTorrentClient torrentClient,
                    SpoolDbContext dbContext,
                    CancellationToken cancellationToken)
        {
            // 1. Get the list of desired games from the local DAT file
            var desiredGames = await _datParser.GetGameNamesFromFileAsync(stream.DatFilePath, cancellationToken);

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
                if (file.Progress >= 1.0f && !string.IsNullOrEmpty(destinationRoot))
                {
                    try
                    {
                        // Note: qBittorrent downloads typically reside in the torrent's save path. 
                        // For local execution, we look for the file relative to the storage setup.
                        string destinationPath = Path.Combine(destinationRoot, file.Name);

                        // If you need a scratch path lookup, ensure it resolves correctly from your client setup,
                        // otherwise direct file management is handled via the target path.

                        // For V1 safety, we ensure the destination directory exists and mark priority 0
                        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

                        filesToSkip.Add(file.Index);
                        continue;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to process completed file {gameName}: {ex.Message}");
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
            if (filesToDownload.Any())
            {
                await torrentClient.SetFilePrioritiesAsync(stream.TorrentIdentifier, filesToDownload, 1, cancellationToken);
            }

            if (filesToSkip.Any())
            {
                await torrentClient.SetFilePrioritiesAsync(stream.TorrentIdentifier, filesToSkip, 0, cancellationToken);
            }

            // 5. Sync state changes to SQLite
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
