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

            // 2. Fetch the current files in the torrent from the BT Client
            var torrentFiles = await torrentClient.GetFilesAsync(stream.TorrentIdentifier, cancellationToken);
            if (torrentFiles == null || !torrentFiles.Any())
            {
                Console.WriteLine($"No files found for torrent {stream.TorrentIdentifier}. It may not be loaded yet.");
                return;
            }

            long currentSpoolFootprint = 0;
            var filesToDownload = new List<int>();
            var filesToSkip = new List<int>();

            // 3. Evaluate every file in the torrent against the DAT list and the Cap
            foreach (var file in torrentFiles)
            {
                // Strip the extension (and folder path) to match the DAT game name
                var gameName = Path.GetFileNameWithoutExtension(file.Name);

                if (desiredGames.Contains(gameName))
                {
                    // If it fits in the cap, queue it. (This includes files already downloaded that are taking up space)
                    if (currentSpoolFootprint + file.Size <= allocatedCapBytes)
                    {
                        filesToDownload.Add(file.Index);
                        currentSpoolFootprint += file.Size;
                    }
                    else
                    {
                        // We want it, but the spool cap is reached for this cycle
                        filesToSkip.Add(file.Index);
                    }
                }
                else
                {
                    // Not in the DAT file, ignore completely
                    filesToSkip.Add(file.Index);
                }
            }

            // 4. Dispatch commands to qBittorrent
            if (filesToDownload.Any())
            {
                await torrentClient.SetFilePrioritiesAsync(stream.TorrentIdentifier, filesToDownload, 1, cancellationToken);
            }

            if (filesToSkip.Any())
            {
                await torrentClient.SetFilePrioritiesAsync(stream.TorrentIdentifier, filesToSkip, 0, cancellationToken);
            }

            // 5. (Future Step) Sync states to dbContext so we can track them in the UI
            // await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
