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
    /// CLI wrapper for the Core <see cref="Core.Commands.ListStreamsCommand"/>.
    /// </summary>
    public class ListStreamsCommand : AsyncCommand<ListStreamsSettings>
    {
        protected override async Task<int> ExecuteAsync(CommandContext context, ListStreamsSettings settings, CancellationToken cancellationToken)
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

            var command = new Core.Commands.ListStreamsCommand(
                serviceProvider.GetRequiredService<IServiceScopeFactory>());

            var streams = await command.ExecuteAsync(settings.Status, cancellationToken);

            StreamTableRenderer.Render(streams);
            return 0;
        }
    }
}
