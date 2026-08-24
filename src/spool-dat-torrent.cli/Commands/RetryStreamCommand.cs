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

            string hash = TorrentMetadataHelper.ResolveInfoHash(settings.Torrent!);

            var command = new Core.Commands.RetryStreamCommand(
                serviceProvider.GetRequiredService<IServiceScopeFactory>());

            var retried = await command.ExecuteAsync(hash, cancellationToken);

            if (retried)
            {
                AnsiConsole.MarkupLine($"[green]Stream re-activated for retry:[/] {Markup.Escape(hash)}");
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]No stream found for:[/] {Markup.Escape(hash)}");
                return 1;
            }

            return 0;
        }
    }
}
