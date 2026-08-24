using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using SpoolDatTorrent.Core.Configuration;
using SpoolDatTorrent.Core.Data;
using SpoolDatTorrent.Core.Helpers;
using SpoolDatTorrent.Core.Interfaces;
using SpoolDatTorrent.Core.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SpoolDatTorrent.Cli.Commands
{
    /// <summary>
    /// Adds a new stream (torrent + DAT) to the database, then starts the spooling monitor.
    /// </summary>
    public class AddStreamCommand : AsyncCommand<SpoolCommandSettings>
    {
        protected override async Task<int> ExecuteAsync(CommandContext context, SpoolCommandSettings settings, CancellationToken cancellationToken)
        {
            Logger.Clear();

            AnsiConsole.MarkupLine($"[green]Adding Torrent:[/] [[{Markup.Escape(Path.GetFileName(settings.Torrent!))}]]");
            AnsiConsole.MarkupLine($"[green]DAT Filter:[/] [[{Markup.Escape(Path.GetFileName(settings.DatPath!))}]]");

            var serviceProvider = CliServiceProvider.Build(settings);

            // Extract hash and seed the DB.
            string calculatedHash = TorrentMetadataHelper.ResolveInfoHash(settings.Torrent!);

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
                        Name = settings.Name ?? GetDefaultStreamName(settings.Torrent!),
                        DatFilePath = settings.DatPath!,
                        SpoolingTargetOverride = settings.TargetOverride,
                        ServerProfileId = "LocalQBit",
                        Status = StreamLifecycleStatus.Active
                    };

                    if (settings.Torrent!.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                    {
                        newStream.OriginalMagnet = settings.Torrent;
                    }
                    else
                    {
                        newStream.OriginalTorrentPath = settings.Torrent;
                    }

                    if (!string.IsNullOrWhiteSpace(settings.Filter))
                    {
                        newStream.FileFilter = settings.Filter;
                    }

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
                    AnsiConsole.MarkupLine($"[green]Added stream:[/] {Markup.Escape(newStream.Name)}");
                }
                else
                {
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
                    AnsiConsole.MarkupLine($"[green]Updated existing stream:[/] {Markup.Escape(existingStream.Name)}");
                }
            }

            // Start the spooling monitor.
            AnsiConsole.MarkupLine("[cyan]Spooling Torrent. Press Ctrl+C to stop...[/]");
            await SpoolMonitor.RunAsync(serviceProvider, cancellationToken);

            return 0;
        }

        private static string GetDefaultStreamName(string torrent)
        {
            if (torrent.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
            {
                return "Magnet Stream";
            }

            var fileName = Path.GetFileName(torrent);
            if (fileName.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
            {
                fileName = fileName[..^".torrent".Length];
            }

            return string.IsNullOrWhiteSpace(fileName) ? "Stream" : fileName;
        }
    }
}
