using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using SpoolDatTorrent.Core.Configuration;
using SpoolDatTorrent.Core.Data;
using SpoolDatTorrent.Core.Interfaces;
using SpoolDatTorrent.Core.Services;
using System.Threading;
using System.Threading.Tasks;

namespace SpoolDatTorrent.Cli.Commands
{
    /// <summary>
    /// CLI wrapper for the Core <see cref="Core.Commands.CancelAllStreamsCommand"/>.
    /// </summary>
    public class CancelAllStreamsCommand : AsyncCommand
    {
        protected override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
        {
            // Destructive: confirm before proceeding.
            if (!AnsiConsole.Confirm("[yellow]This will remove ALL torrents from qBittorrent and clear all streams. Continue?[/]"))
            {
                AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
                return 0;
            }

            var configuration = new ConfigurationBuilder()
                .AddJsonFile(SettingsManager.GetSettingsPath(), optional: false, reloadOnChange: true)
                .Build();

            var services = new ServiceCollection();
            services.Configure<GlobalSpoolSettings>(configuration);
            services.AddDbContext<SpoolDbContext>(options => options.UseSqlite("DataSource=spooldattorrent.db"));
            services.AddHttpClient();
            services.AddSingleton<IBitTorrentClientFactory, BitTorrentClientFactory>();

            var serviceProvider = services.BuildServiceProvider();

            var command = new Core.Commands.CancelAllStreamsCommand(
                serviceProvider.GetRequiredService<IBitTorrentClientFactory>(),
                serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<GlobalSpoolSettings>>());

            var removed = await command.ExecuteAsync(cancellationToken);

            AnsiConsole.MarkupLine($"[green]Cancelled all streams. Removed {removed} torrent(s) from the client.[/]");
            return 0;
        }
    }
}
