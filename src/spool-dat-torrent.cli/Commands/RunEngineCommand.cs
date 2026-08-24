using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using SpoolDatTorrent.Core.Helpers;
using System.Threading;
using System.Threading.Tasks;

namespace SpoolDatTorrent.Cli.Commands
{
    /// <summary>
    /// Default command: lists server profiles and streams, then starts the spooling monitor.
    /// </summary>
    public class RunEngineCommand : AsyncCommand<MonitorSettings>
    {
        protected override async Task<int> ExecuteAsync(CommandContext context, MonitorSettings settings, CancellationToken cancellationToken)
        {
            Logger.Clear();

            var serviceProvider = CliServiceProvider.Build();

            // List server profiles and streams before starting the monitor.
            await StreamTableRenderer.ShowServersAndStreamsAsync(serviceProvider, cancellationToken);

            AnsiConsole.MarkupLine("[cyan]Spooling Torrent/s. Please wait for live updates. Log for details. Press Ctrl+C to stop...[/]");

            await SpoolMonitor.RunAsync(serviceProvider, cancellationToken);

            return 0;
        }
    }
}
