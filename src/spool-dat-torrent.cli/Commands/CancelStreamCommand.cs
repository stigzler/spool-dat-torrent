using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using SpoolDatTorrent.Core.Configuration;
using SpoolDatTorrent.Core.Data;
using SpoolDatTorrent.Core.Helpers;
using SpoolDatTorrent.Core.Interfaces;
using SpoolDatTorrent.Core.Services;
using System.Threading;
using System.Threading.Tasks;

namespace SpoolDatTorrent.Cli.Commands
{
    /// <summary>
    /// CLI wrapper for the Core <see cref="CancelStreamCommand"/>.
    /// </summary>
    public class CancelStreamCommand : AsyncCommand<CancelStreamSettings>
    {
        protected override async Task<int> ExecuteAsync(CommandContext context, CancelStreamSettings settings, CancellationToken cancellationToken)
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile(SettingsManager.GetSettingsPath(), optional: false, reloadOnChange: true)
                .Build();

            var services = new ServiceCollection();
            services.Configure<GlobalSpoolSettings>(configuration);
            services.AddDbContext<SpoolDbContext>(options => options.UseSqlite("DataSource=cli_test.db"));
            services.AddHttpClient();
            services.AddSingleton<IBitTorrentClientFactory, BitTorrentClientFactory>();

            var serviceProvider = services.BuildServiceProvider();

            string hash = TorrentMetadataHelper.ResolveInfoHash(settings.Torrent!);

            var command = new Core.Commands.CancelStreamCommand(
                serviceProvider.GetRequiredService<IBitTorrentClientFactory>(),
                serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<GlobalSpoolSettings>>());

            var removed = await command.ExecuteAsync(hash, cancellationToken);

            if (removed)
            {
                AnsiConsole.MarkupLine($"[green]Cancelled stream:[/] {Markup.Escape(hash)}");
            }
            else
            {
                AnsiConsole.MarkupLine($"[yellow]No stream found for:[/] {Markup.Escape(hash)}");
            }

            return 0;
        }
    }
}
