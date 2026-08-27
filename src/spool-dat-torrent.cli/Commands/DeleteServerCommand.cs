using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using SpoolDatTorrent.Core.Commands;
using SpoolDatTorrent.Core.Helpers;
using System.Threading;
using System.Threading.Tasks;

namespace SpoolDatTorrent.Cli.Commands
{
    /// <summary>
    /// Deletes a BitTorrent server profile by name.
    /// </summary>
    public class DeleteServerCommand : AsyncCommand<DeleteServerSettings>
    {
        protected override async Task<int> ExecuteAsync(CommandContext context, DeleteServerSettings settings, CancellationToken cancellationToken)
        {
            var serviceProvider = CliServiceProvider.Build();

            var command = serviceProvider.GetRequiredService<DeleteServerProfileCommand>();
            var result = await command.ExecuteAsync(settings.Name!, cancellationToken);

            if (result.Success)
            {
                AnsiConsole.MarkupLine($"[green]{Markup.Escape(result.Message)}[/]");
                Logger.Log(result.Message);
                return 0;
            }

            AnsiConsole.MarkupLine($"[red]{Markup.Escape(result.Message)}[/]");
            Logger.LogError(result.Message);
            return 1;
        }
    }
}
