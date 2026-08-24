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
using SpoolDatTorrent.Cli.Services;
using System;
using System.Collections.Generic;
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

            AnsiConsole.MarkupLine($"[green]Spooling Torrent:[/] [[{Markup.Escape(Path.GetFileName(settings.Torrent!))}]]");
            AnsiConsole.MarkupLine($"[green]DAT Filter:[/] [[{Markup.Escape(Path.GetFileName(settings.DatPath!))}]]");

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
            services.AddSingleton<ISpoolingProgressReporter, SpectreProgressReporter>();
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
            AnsiConsole.MarkupLine("[cyan]Spooling Torrent. Press Ctrl+C to stop...[/]");
            var engine = serviceProvider.GetRequiredService<SpoolingEngine>();
            var reporter = (SpectreProgressReporter)serviceProvider.GetRequiredService<ISpoolingProgressReporter>();

            // Poll interval (seconds) drives how often the engine queries qBittorrent for
            // file progress. This is the granularity of the download % shown in the UI.
            var pollInterval = serviceProvider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<GlobalSpoolSettings>>()
                .Value.PollIntervalSeconds;

            // Run the engine loop in the background at the configured poll cadence.
            var engineTask = Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await engine.EvaluateAllStreamsAsync(cancellationToken);
                    await Task.Delay(TimeSpan.FromSeconds(pollInterval), cancellationToken);
                }
            }, cancellationToken);

            // Real Spectre Progress widget: the status message is shown as an indeterminate
            // task at the top, and the current batch's files are the progress tasks below.
            // Both live in this single live region, so both refresh once per second.
            await AnsiConsole.Progress()
                .AutoClear(false)
                .AutoRefresh(true)
                .HideCompleted(true)
                .Columns(new ProgressColumn[]
                {
                    new SpinnerColumn(Spinner.Known.Dots),
                    new RemainingTimeColumn() ,
                    new PercentageColumn(),
                    new ProgressBarColumn() { Width = 30},
                    new TaskDescriptionColumn() { Alignment = Justify.Left}
                })
                .StartAsync(async ctx =>
                {
                    // Spectre renders tasks in the order they are added, and that order is
                    // fixed. To get Jobs (top) -> Status (middle) -> Files (bottom), we add
                    // the status task lazily, only after the first job task exists.
                    ProgressTask? statusTask = null;

                    var tasks = new Dictionary<string, ProgressTask>(StringComparer.OrdinalIgnoreCase);
                    var jobTasks = new Dictionary<string, ProgressTask>(StringComparer.OrdinalIgnoreCase);

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        // 1. Job-level completion (moved / total desired files), one task per stream.
                        foreach (var stream in reporter.GetStreams())
                        {
                            var description = $"[yellow]{stream.Name} — {stream.MovedCount} / {stream.TotalCount} files processed[/]";
                            if (!jobTasks.TryGetValue(stream.TorrentIdentifier, out var jobTask))
                            {
                                jobTask = jobTasks[stream.TorrentIdentifier] = ctx.AddTask(description, maxValue: stream.TotalCount > 0 ? stream.TotalCount : 1);
                            }
                            jobTask.Description = description;
                            jobTask.MaxValue = stream.TotalCount > 0 ? stream.TotalCount : 1;
                            jobTask.Value = stream.MovedCount;
                        }

                        // Remove job tasks for streams no longer reported.
                        var activeJobs = new HashSet<string>(reporter.GetStreams().Select(s => s.TorrentIdentifier), StringComparer.OrdinalIgnoreCase);
                        foreach (var id in jobTasks.Keys.Where(k => !activeJobs.Contains(k)).ToList())
                        {
                            jobTasks[id].StopTask();
                            jobTasks.Remove(id);
                        }

                        // 2. Status line (middle) — created only after jobs exist so it sits below them.
                        if (statusTask == null && jobTasks.Count > 0)
                        {
                            statusTask = ctx.AddTask("Initialising...", maxValue: 100);
                            statusTask.IsIndeterminate();
                        }
                        if (statusTask != null)
                        {
                            statusTask.Description = reporter.GetStatus();
                        }

                        // 3. Files (bottom).
                        var files = reporter.GetFiles();

                        foreach (var file in files)
                        {
                            if (!tasks.TryGetValue(file.Name, out var task))
                            {
                                task = tasks[file.Name] = ctx.AddTask($"[Grey46]{file.Name}[/]", maxValue: 100);
                            }
                            task.Value = Math.Clamp(file.Progress * 100, 0, 100);
                        }

                        // Remove tasks for files no longer in the batch.
                        var active = new HashSet<string>(files.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
                        foreach (var name in tasks.Keys.Where(k => !active.Contains(k)).ToList())
                        {
                            tasks[name].StopTask();
                            tasks.Remove(name);
                        }

                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    }
                });

            await engineTask;
            return 0;
        }
    }
}