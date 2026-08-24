using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using SpoolDatTorrent.Core.Helpers;
using SpoolDatTorrent.Core.Models;
using System.Threading;
using System.Threading.Tasks;

namespace SpoolDatTorrent.Cli.Commands
{
    /// <summary>
    /// Pauses a stream (sets its status to Paused so the engine stops spooling it).
    /// </summary>
    public class PauseStreamCommand : AsyncCommand<SetStreamStatusSettings>
    {
        protected override async Task<int> ExecuteAsync(CommandContext context, SetStreamStatusSettings settings, CancellationToken cancellationToken)
        {
            var serviceProvider = CliServiceProvider.Build();
            var command = new Core.Commands.SetStreamStatusCommand(serviceProvider.GetRequiredService<IServiceScopeFactory>());

            string identifier = settings.Identifier!;
            bool updated = int.TryParse(identifier, out int streamId)
                ? await command.ExecuteByIdAsync(streamId, StreamLifecycleStatus.Paused, cancellationToken)
                : await command.ExecuteAsync(TorrentMetadataHelper.ResolveInfoHash(identifier), StreamLifecycleStatus.Paused, cancellationToken);

            if (updated)
            {
                AnsiConsole.MarkupLine($"[green]Stream paused:[/] {Markup.Escape(identifier)}");
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
