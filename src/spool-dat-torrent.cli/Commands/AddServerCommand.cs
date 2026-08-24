using Spectre.Console;
using Spectre.Console.Cli;
using SpoolDatTorrent.Core.Configuration;
using System.Threading.Tasks;

namespace SpoolDatTorrent.Cli.Commands
{
    /// <summary>
    /// Creates a new BitTorrent server profile in the settings file.
    /// </summary>
    public class AddServerCommand : AsyncCommand
    {
        protected override Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
        {
            var profileName = SettingsManager.AddServerProfile();

            AnsiConsole.MarkupLine($"[green]Created new server profile:[/] {Markup.Escape(profileName)}");
            AnsiConsole.MarkupLine($"[grey]Edit {Markup.Escape(SettingsManager.GetSettingsPath())} to configure it.[/]");

            return Task.FromResult(0);
        }
    }
}

