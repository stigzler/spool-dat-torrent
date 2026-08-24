using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using SpoolDatTorrent.Core.Helpers;
using System.Threading;
using System.Threading.Tasks;

namespace SpoolDatTorrent.Cli.Commands
{
    /// <summary>
    /// Default command: lists all streams, then starts the spooling monitor.
    /// </summary>
    public class RunEngineCommand : AsyncCommand<MonitorSettings>
    {
        protected override async Task<int> ExecuteAsync(CommandContext context, MonitorSettings settings, CancellationToken cancellationToken)
        {
            Logger.Clear();

            var serviceProvider = CliServiceProvider.Build();

            // List all streams before starting the monitor.
            var listCommand = new Core.Commands.ListStreamsCommand(
                serviceProvider.GetRequiredService<IServiceScopeFactory>());
            var streams = await listCommand.ExecuteAsync(cancellationToken: cancellationToken);
            StreamTableRenderer.Render(streams);

            AnsiConsole.MarkupLine("[cyan]Spooling Torrent/s. Please wait for live updates. Press Ctrl+C to stop...[/]");

            await SpoolMonitor.RunAsync(serviceProvider, cancellationToken);

            return 0;
        }
    }
}
