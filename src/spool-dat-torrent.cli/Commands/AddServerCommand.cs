using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using SpoolDatTorrent.Core.Commands;
using SpoolDatTorrent.Core.Configuration;
using System.Threading;
using System.Threading.Tasks;

namespace SpoolDatTorrent.Cli.Commands
{
    /// <summary>
    /// Creates a new BitTorrent server profile in the settings file.
    /// </summary>
    public class AddServerCommand : AsyncCommand
    {
        protected override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
        {
            var serviceProvider = CliServiceProvider.Build();

            var command = serviceProvider.GetRequiredService<AddServerProfileCommand>();
            var result = await command.ExecuteAsync(cancellationToken);

            AnsiConsole.MarkupLine($"[green]{Markup.Escape(result.Message)}[/]");
            AnsiConsole.MarkupLine($"[grey]Edit {Markup.Escape(SettingsManager.GetSettingsPath())} to configure it.[/]");

            return result.Success ? 0 : 1;
        }
    }
}
