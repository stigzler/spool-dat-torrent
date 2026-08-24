using Spectre.Console;
using Spectre.Console.Cli;
using SpoolDatTorrent.Core.Configuration;
using System.Threading;
using System.Threading.Tasks;

namespace SpoolDatTorrent.Cli.Commands
{
    /// <summary>
    /// Deletes a BitTorrent server profile by name.
    /// </summary>
    public class DeleteServerCommand : AsyncCommand<DeleteServerSettings>
    {
        protected override Task<int> ExecuteAsync(CommandContext context, DeleteServerSettings settings, CancellationToken cancellationToken)
        {
            var removed = SettingsManager.DeleteServerProfile(settings.Name!);

            if (removed)
            {
                AnsiConsole.MarkupLine($"[green]Deleted server profile:[/] {Markup.Escape(settings.Name!)}");
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]Server profile not found:[/] {Markup.Escape(settings.Name!)}");
                return Task.FromResult(1);
            }

            return Task.FromResult(0);
        }
    }
}
