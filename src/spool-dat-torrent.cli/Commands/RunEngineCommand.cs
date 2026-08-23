using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using SpoolDatTorrent.Core.Configuration;
using SpoolDatTorrent.Core.Data;
using SpoolDatTorrent.Core.Helpers;
using SpoolDatTorrent.Core.Interfaces;
using SpoolDatTorrent.Core.Models;
using SpoolDatTorrent.Core.Services;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SpoolDatTorrent.Cli.Commands
{
    public class RunEngineCommand : AsyncCommand<SpoolCommandSettings>
    {
        protected override async Task<int> ExecuteAsync(CommandContext context, SpoolCommandSettings settings, CancellationToken cancellationToken)
        {
            Logger.Clear();

            AnsiConsole.MarkupLine($"[green]Preparing to spool:[/] {Markup.Escape(settings.Torrent!)}");
            AnsiConsole.MarkupLine($"[green]Using DAT:[/] {Markup.Escape(settings.DatPath!)}");

            // 1. Load Configuration
            var configuration = new ConfigurationBuilder()
                .AddJsonFile(SettingsManager.GetSettingsPath(), optional: false, reloadOnChange: true)
                .Build();

            // 2. Build Dependency Injection
            var services = new ServiceCollection();

            services.Configure<GlobalSpoolSettings>(configuration);

            // If overrides were passed via CLI, force them into memory for this run
            if (settings.CapOverride.HasValue || !string.IsNullOrWhiteSpace(settings.ClientHost) || !string.IsNullOrWhiteSpace(settings.ClientKey))
            {
                AnsiConsole.MarkupLine("[yellow]Applying CLI configuration overrides...[/]");
                services.PostConfigure<GlobalSpoolSettings>(opt =>
                {
                    if (opt.TorrentServers.TryGetValue("LocalQBit", out var profile))
                    {
                        if (settings.CapOverride.HasValue) profile.SpoolingCapGb = settings.CapOverride.Value;
                        if (!string.IsNullOrWhiteSpace(settings.ClientHost)) profile.Host = settings.ClientHost;
                        if (!string.IsNullOrWhiteSpace(settings.ClientKey)) profile.ApiKey = settings.ClientKey;
                    }
                });
            }

            services.AddDbContext<SpoolDbContext>(options => options.UseSqlite("DataSource=cli_test.db"));
            services.AddHttpClient();
            services.AddSingleton<IBitTorrentClientFactory, BitTorrentClientFactory>();
            services.AddTransient<IDatParserService, LogiqxDatParserService>();
            services.AddTransient<SpoolingEngine>();

            var serviceProvider = services.BuildServiceProvider();

            // 3. Extract Hash & Seed DB
            string calculatedHash = TorrentMetadataHelper.GetInfoHash(settings.Torrent!);

            // 3a. "Start fresh": remove the torrent from the client (and its scratch files)
            //     and clear the saved stream, so the next run re-adds from scratch. Files
            //     already moved to the destination are KEPT (the engine re-detects them on
            //     disk and skips re-downloading them).
            if (settings.Fresh)
            {
                AnsiConsole.MarkupLine("[yellow]Starting fresh: removing torrent from client and clearing saved state...[/]");

                var clientFactory = serviceProvider.GetRequiredService<IBitTorrentClientFactory>();
                var freshClient = clientFactory.GetClient("LocalQBit");

                try
                {
                    await freshClient.AuthenticateAsync(cancellationToken);
                    await freshClient.DeleteTorrentAsync(calculatedHash, deleteFiles: true, cancellationToken);
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[yellow]Warning:[/] Could not remove torrent from client: {Markup.Escape(ex.Message)}");
                }

                using (var freshScope = serviceProvider.CreateScope())
                {
                    var freshDb = freshScope.ServiceProvider.GetRequiredService<SpoolDbContext>();
                    var toDelete = await freshDb.Streams.FirstOrDefaultAsync(s => s.TorrentIdentifier == calculatedHash, cancellationToken);
                    if (toDelete != null)
                    {
                        freshDb.Streams.Remove(toDelete);
                        await freshDb.SaveChangesAsync(cancellationToken);
                    }
                }
            }

            using (var scope = serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<SpoolDbContext>();
                await db.Database.EnsureCreatedAsync(cancellationToken);

                var existingStream = await db.Streams.FirstOrDefaultAsync(s => s.TorrentIdentifier == calculatedHash, cancellationToken);

                if (existingStream == null)
                {
                    var newStream = new TorrentStreamItem
                    {
                        TorrentIdentifier = calculatedHash,
                        Name = settings.Name ?? "CLI Automated Injection",
                        DatFilePath = settings.DatPath!,
                        SpoolingTargetOverride = settings.TargetOverride,
                        ServerProfileId = "LocalQBit",
                        Status = StreamLifecycleStatus.Active
                    };

                    // Persist the original torrent source so the engine can re-add it after deletion
                    if (settings.Torrent!.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                    {
                        newStream.OriginalMagnet = settings.Torrent;
                    }
                    else
                    {
                        newStream.OriginalTorrentPath = settings.Torrent;
                    }

                    // Map the Filter directly
                    if (!string.IsNullOrWhiteSpace(settings.Filter))
                    {
                        newStream.FileFilter = settings.Filter;
                    }

                    // Parse and map the Strategy Enum safely
                    if (!string.IsNullOrWhiteSpace(settings.Strategy))
                    {
                        if (Enum.TryParse<SpoolingStrategy>(settings.Strategy, ignoreCase: true, out var parsedStrategy))
                        {
                            newStream.Strategy = parsedStrategy;
                        }
                        else
                        {
                            AnsiConsole.MarkupLine($"[yellow]Warning:[/] Strategy '{Markup.Escape(settings.Strategy)}' not recognized. Falling back to default.");
                        }
                    }

                    db.Streams.Add(newStream);
                    await db.SaveChangesAsync(cancellationToken);
                }
                else
                {
                    // Stream already exists: refresh its mutable fields so the latest CLI
                    // args win (otherwise a stale --target from a previous run persists).
                    existingStream.Name = settings.Name ?? existingStream.Name;
                    existingStream.DatFilePath = settings.DatPath!;
                    existingStream.SpoolingTargetOverride = settings.TargetOverride;
                    existingStream.Status = StreamLifecycleStatus.Active;

                    if (!string.IsNullOrWhiteSpace(settings.Filter))
                    {
                        existingStream.FileFilter = settings.Filter;
                    }

                    if (!string.IsNullOrWhiteSpace(settings.Strategy) &&
                        Enum.TryParse<SpoolingStrategy>(settings.Strategy, ignoreCase: true, out var parsedStrategy))
                    {
                        existingStream.Strategy = parsedStrategy;
                    }

                    await db.SaveChangesAsync(cancellationToken);
                }
            }

            // 4. Execute Engine continuously. The engine is responsible for adding the
            //    torrent (via its recovery path) and for all client interaction, so a
            //    down/unreachable qBittorrent is handled gracefully in Core rather than
            //    crashing the CLI.
            AnsiConsole.MarkupLine("[cyan]Starting continuous SpoolDatTorrent evaluation loop. Press Ctrl+C to stop...[/]");
            var engine = serviceProvider.GetRequiredService<SpoolingEngine>();

            while (!cancellationToken.IsCancellationRequested)
            {
                await engine.EvaluateAllStreamsAsync(cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken); // Wait before polling again
            }

            return 0;
        }
    }
}