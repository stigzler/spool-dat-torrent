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
    /// The stream creation itself is delegated to the Core command so all UIs share it.
    /// </summary>
    public class AddStreamCommand : AsyncCommand<SpoolCommandSettings>
    {
        protected override async Task<int> ExecuteAsync(CommandContext context, SpoolCommandSettings settings, CancellationToken cancellationToken)
        {
            Logger.Clear();

            AnsiConsole.MarkupLine($"[green]Adding Torrent:[/] [[{Markup.Escape(Path.GetFileName(settings.Torrent!))}]]");
            AnsiConsole.MarkupLine($"[green]DAT Filter:[/] [[{Markup.Escape(Path.GetFileName(settings.DatPath!))}]]");

            var serviceProvider = CliServiceProvider.Build(settings);

            // Resolve which server profile to use: explicit --server, else the configured default.
            var globalSettings = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<GlobalSpoolSettings>>().Value;
            string resolvedServer = !string.IsNullOrWhiteSpace(settings.Server)
                ? settings.Server
                : globalSettings.DefaultServerProfile;

            if (!globalSettings.TorrentServers.ContainsKey(resolvedServer))
            {
                var error = $"Server profile '{resolvedServer}' does not exist. Use 'spool list' to see available profiles, or 'spool add-server' to create one.";
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
                Logger.Log($"[Error] {error}");
                return 1;
            }

            // Extract hash and seed the DB.
            string calculatedHash = TorrentMetadataHelper.ResolveInfoHash(settings.Torrent!);

            if (settings.Fresh)
            {
                AnsiConsole.MarkupLine("[yellow]Starting fresh: removing torrent from client and clearing saved state...[/]");

                var clientFactory = serviceProvider.GetRequiredService<IBitTorrentClientFactory>();
                var freshClient = clientFactory.GetClient(resolvedServer);

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

            // Parse optional strategy.
            SpoolingStrategy? strategy = null;
            if (!string.IsNullOrWhiteSpace(settings.Strategy))
            {
                if (Enum.TryParse<SpoolingStrategy>(settings.Strategy, ignoreCase: true, out var parsed))
                {
                    strategy = parsed;
                }
                else
                {
                    AnsiConsole.MarkupLine($"[yellow]Warning:[/] Strategy '{Markup.Escape(settings.Strategy)}' not recognized. Falling back to default.");
                }
            }

            var name = settings.Name ?? GetDefaultStreamName(settings.Torrent!);
            var isMagnet = settings.Torrent!.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase);

            var addCommand = new Core.Commands.AddStreamCommand(
                serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<GlobalSpoolSettings>>());

            var stream = await addCommand.ExecuteAsync(
                torrentIdentifier: calculatedHash,
                datFilePath: settings.DatPath!,
                name: name,
                spoolingTargetOverride: settings.TargetOverride,
                // Pass the explicit --server (may be null); Core preserves the existing
                // profile on update when this is null/empty.
                serverProfileId: settings.Server,
                originalTorrentPath: isMagnet ? null : settings.Torrent,
                originalMagnet: isMagnet ? settings.Torrent : null,
                filter: settings.Filter,
                strategy: strategy,
                cancellationToken: cancellationToken);

            // Show the server profiles and streams (like "list") before starting the monitor.
            AnsiConsole.WriteLine();
            await StreamTableRenderer.ShowServersAndStreamsAsync(serviceProvider, cancellationToken);

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
