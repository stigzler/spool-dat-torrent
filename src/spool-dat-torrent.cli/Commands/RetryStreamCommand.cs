using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using SpoolDatTorrent.Core.Helpers;
using System.Threading;
using System.Threading.Tasks;

namespace SpoolDatTorrent.Cli.Commands
{
    /// <summary>
    /// CLI wrapper for the Core <see cref="Core.Commands.RetryStreamCommand"/>.
    /// </summary>
    public class RetryStreamCommand : AsyncCommand<RetryStreamSettings>
    {
        protected override async Task<int> ExecuteAsync(CommandContext context, RetryStreamSettings settings, CancellationToken cancellationToken)
        {
            var serviceProvider = CliServiceProvider.Build();

            var command = new Core.Commands.RetryStreamCommand(
                serviceProvider.GetRequiredService<IServiceScopeFactory>());

            string identifier = settings.Identifier!;

            // A plain integer is treated as a stream Id; anything else as a torrent
            // path/magnet/info-hash.
            bool retried = int.TryParse(identifier, out int streamId)
                ? await command.ExecuteByIdAsync(streamId, cancellationToken)
                : await command.ExecuteAsync(TorrentMetadataHelper.ResolveInfoHash(identifier), cancellationToken);

            if (retried)
            {
                AnsiConsole.MarkupLine($"[green]Stream re-activated for retry:[/] {Markup.Escape(identifier)}");
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]No stream found for:[/] {Markup.Escape(identifier)}");
                return 1;
            }

            return 0;
        }
    }
}
