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

            using (var scope = serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<SpoolDbContext>();
                await db.Database.EnsureCreatedAsync(cancellationToken);

                if (!db.Streams.Any(s => s.TorrentIdentifier == calculatedHash))
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
            }

            // 4. Inject Torrent to qBittorrent
            AnsiConsole.MarkupLine("[dim]Injecting torrent into qBittorrent...[/]");
            var clientFactory = serviceProvider.GetRequiredService<IBitTorrentClientFactory>();
            var qbitClient = clientFactory.GetClient("LocalQBit");

            await qbitClient.AuthenticateAsync(cancellationToken);
            await qbitClient.AddTorrentAsync(settings.Torrent!, settings.TargetOverride, cancellationToken);

            AnsiConsole.MarkupLine("[dim]Waiting 5 seconds for qBittorrent to parse the file tree...[/]");
            await Task.Delay(5000, cancellationToken);

            // 5. Execute Engine
            AnsiConsole.MarkupLine("[cyan]Starting SpoolDatTorrent evaluation...[/]");
            var engine = serviceProvider.GetRequiredService<SpoolingEngine>();
            await engine.EvaluateAllStreamsAsync(cancellationToken);

            AnsiConsole.MarkupLine("[green]Evaluation complete. Check qBittorrent![/]");
            return 0;
        }
    }
}