using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using SpoolDatTorrent.Core.Configuration;
using SpoolDatTorrent.Core.Data;
using SpoolDatTorrent.Core.Helpers;
using SpoolDatTorrent.Core.Interfaces;
using SpoolDatTorrent.Core.Services;
using System.Threading;
using System.Threading.Tasks;

namespace SpoolDatTorrent.Cli.Commands
{
    /// <summary>
    /// CLI wrapper for the Core <see cref="CancelStreamCommand"/>.
    /// </summary>
    public class CancelStreamCommand : AsyncCommand<CancelStreamSettings>
    {
        protected override async Task<int> ExecuteAsync(CommandContext context, CancelStreamSettings settings, CancellationToken cancellationToken)
        {
            var serviceProvider = CliServiceProvider.Build();

            var command = new Core.Commands.CancelStreamCommand(
                serviceProvider.GetRequiredService<IBitTorrentClientFactory>(),
                serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<GlobalSpoolSettings>>());

            string identifier = settings.Identifier!;

            // A plain integer is treated as a stream Id; anything else as a torrent
            // path/magnet/info-hash.
            bool removed = int.TryParse(identifier, out int streamId)
                ? await command.ExecuteByIdAsync(streamId, cancellationToken)
                : await command.ExecuteAsync(TorrentMetadataHelper.ResolveInfoHash(identifier), cancellationToken);

            if (removed)
            {
                AnsiConsole.MarkupLine($"[green]Cancelled stream:[/] {Markup.Escape(identifier)}");
            }
            else
            {
                AnsiConsole.MarkupLine($"[yellow]No stream found for:[/] {Markup.Escape(identifier)}");
            }

            return removed ? 0 : 1;
        }
    }
}
