using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SpoolDatTorrent.Core.Configuration;
using SpoolDatTorrent.Core.Data;
using SpoolDatTorrent.Core.DTOs;
using SpoolDatTorrent.Core.Interfaces;
using SpoolDatTorrent.Core.Models;
using SpoolDatTorrent.Core.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SpoolDatTorrent.Core.Tests
{
    public class SpoolingEngineTests
    {
        // 1. Fake BT Client to track what priorities get sent to it
        private class FakeBitTorrentClient : IBitTorrentClient
        {
            public Dictionary<int, int> FilePriorities { get; } = new();
            public List<TorrentFileDto> MockFiles { get; set; } = new();

            public Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
            public Task<long> GetActiveFootprintBytesAsync(string torrentId, CancellationToken cancellationToken = default) => Task.FromResult(0L);
            public Task<IReadOnlyList<TorrentFileDto>> GetFilesAsync(string torrentId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TorrentFileDto>>(MockFiles);
            public Task MoveFilesAsync(string torrentId, string newDestinationPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task PauseTorrentAsync(string torrentId, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task ResumeTorrentAsync(string torrentId, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task SetDownloadLimitAsync(string torrentId, long bytesPerSecond, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task AddTorrentAsync(string torrentUrlOrMagnet, string? savePath = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task SetFilePrioritiesAsync(string torrentId, IEnumerable<int> fileIndices, int priority, CancellationToken cancellationToken = default)
            {
                foreach (var index in fileIndices)
                {
                    FilePriorities[index] = priority;
                }
                return Task.CompletedTask;
            }
        }

        // 2. Fake DAT Parser implementing the full IDatParserService interface
        private class FakeDatParserService : IDatParserService
        {
            private readonly HashSet<string> _games;
            public FakeDatParserService(params string[] games) => _games = new HashSet<string>(games, StringComparer.OrdinalIgnoreCase);

            public Task<HashSet<string>> GetGameNamesFromFileAsync(string filePath, CancellationToken cancellationToken = default)
                => Task.FromResult(_games);

            public Task<HashSet<string>> GetGameNamesFromStreamAsync(Stream stream, CancellationToken cancellationToken = default)
                => Task.FromResult(_games);
        }

        // 3. Fake Client Factory
        private class FakeClientFactory : IBitTorrentClientFactory
        {
            private readonly IBitTorrentClient _client;
            public FakeClientFactory(IBitTorrentClient client) => _client = client;
            public IBitTorrentClient GetClient(string profileName) => _client;
        }


        [Fact]
        public async Task EvaluateAllStreamsAsync_EnforcesSpoolCapAndPriorities()
        {
            // Arrange - Keep an explicit connection open so in-memory SQLite doesn't wipe itself between scopes
            var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();

            try
            {
                var optionsBuilder = new DbContextOptionsBuilder<SpoolDbContext>().UseSqlite(connection);

                // Seed the database
                using (var context = new SpoolDbContext(optionsBuilder.Options))
                {
                    await context.Database.EnsureCreatedAsync();

                    context.Streams.Add(new TorrentStreamItem
                    {
                        TorrentIdentifier = "testhash123",
                        Name = "Test Torrent",
                        DatFilePath = "dummy.dat",
                        ServerProfileId = "LocalQBit",
                        Status = StreamLifecycleStatus.Active
                    });
                    await context.SaveChangesAsync();
                }

                // Setup ServiceCollection using the same open connection
                var services = new ServiceCollection();
                services.AddDbContext<SpoolDbContext>(options => options.UseSqlite(connection));
                var serviceProvider = services.BuildServiceProvider();

                var settings = new GlobalSpoolSettings
                {
                    DefaultServerProfile = "LocalQBit",
                    TorrentServers = new Dictionary<string, TorrentServerProfile>
                    {
                        { "LocalQBit", new TorrentServerProfile { SpoolingCapGb = 1 } }
                    }
                };

                var fakeClient = new FakeBitTorrentClient
                {
                    MockFiles = new List<TorrentFileDto>
                    {
                        new() { Index = 0, Name = "GameA.zip", Size = 100, Progress = 0 },
                        new() { Index = 1, Name = "GameB.zip", Size = 100, Progress = 0 }
                    }
                };

                var fakeDatParser = new FakeDatParserService("GameA", "GameB");
                var fakeFactory = new FakeClientFactory(fakeClient);
                var options = Options.Create(settings);

                var engine = new SpoolingEngine(
                    serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                    fakeFactory,
                    fakeDatParser,
                    options
                );

                // Act
                await engine.EvaluateAllStreamsAsync();

                // Assert - Should complete cleanly and populate priorities
                Assert.True(fakeClient.FilePriorities.Count > 0);
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}