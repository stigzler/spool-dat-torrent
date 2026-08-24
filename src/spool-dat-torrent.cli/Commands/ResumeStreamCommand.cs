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
    /// Resumes a paused stream (sets its status to Active so the engine spools it again).
    /// </summary>
    public class ResumeStreamCommand : AsyncCommand<SetStreamStatusSettings>
    {
        protected override async Task<int> ExecuteAsync(CommandContext context, SetStreamStatusSettings settings, CancellationToken cancellationToken)
        {
            var serviceProvider = CliServiceProvider.Build();
            var command = new Core.Commands.SetStreamStatusCommand(serviceProvider.GetRequiredService<IServiceScopeFactory>());

            string identifier = settings.Identifier!;
            bool updated = int.TryParse(identifier, out int streamId)
                ? await command.ExecuteByIdAsync(streamId, StreamLifecycleStatus.Active, cancellationToken)
                : await command.ExecuteAsync(TorrentMetadataHelper.ResolveInfoHash(identifier), StreamLifecycleStatus.Active, cancellationToken);

            if (updated)
            {
                AnsiConsole.MarkupLine($"[green]Stream resumed:[/] {Markup.Escape(identifier)}");
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
