using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SpoolDatTorrent.Core.Commands;
using SpoolDatTorrent.Core.Configuration;
using SpoolDatTorrent.Core.Data;
using SpoolDatTorrent.Core.Helpers;
using SpoolDatTorrent.Core.Interfaces;
using SpoolDatTorrent.Core.Services;
using SpoolDatTorrent.Cli.Services;

namespace SpoolDatTorrent.Cli.Commands
{
    /// <summary>
    /// Builds the shared service provider used by the CLI commands.
    /// </summary>
    internal static class CliServiceProvider
    {
        public static ServiceProvider Build(SpoolCommandSettings? settings = null)
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile(SettingsManager.GetSettingsPath(), optional: false, reloadOnChange: true)
                .Build();

            var services = new ServiceCollection();
            services.Configure<GlobalSpoolSettings>(configuration);

            // Apply CLI overrides BEFORE the provider is built, so the IOptions snapshot
            // reflects them (rather than mutating the profile object after the fact).
            if (settings != null &&
                (settings.CapOverride.HasValue || !string.IsNullOrWhiteSpace(settings.ClientHost) || !string.IsNullOrWhiteSpace(settings.ClientKey)))
            {
                services.PostConfigure<GlobalSpoolSettings>(opt =>
                {
                    if (opt.TorrentServers.TryGetValue("LocalQBit", out var profile))
                    {
                        if (settings.CapOverride.HasValue) profile.SpoolingCapGb = settings.CapOverride.Value;
                        if (!string.IsNullOrWhiteSpace(settings.ClientHost)) profile.Host = settings.ClientHost;
                        if (!string.IsNullOrWhiteSpace(settings.ClientKey)) profile.ApiKey = settings.ClientKey;
                    }
                });
            }

            services.AddDbContext<SpoolDbContext>(options => options.UseSqlite($"DataSource={SettingsManager.GetDatabasePath()}"));
            services.AddHttpClient();
            services.AddSingleton<IBitTorrentClientFactory, BitTorrentClientFactory>();
            services.AddSingleton<IDatParserService, LogiqxDatParserService>();
            services.AddSingleton<ISpoolingProgressReporter, SpectreProgressReporter>();
            services.AddSingleton<SpoolingEngine>();
            services.AddTransient<DeleteServerProfileCommand>();
            services.AddTransient<AddServerProfileCommand>();
            services.AddTransient<EditStreamCommand>();

            var provider = services.BuildServiceProvider();

            // Apply any pending EF migrations so existing databases are upgraded seamlessly
            // (the Core commands also migrate on demand, but doing it here covers any direct
            // DbContext usage, e.g. the "fresh" path in add).
            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<SpoolDbContext>();
                db.Database.Migrate();
            }

            return provider;
        }
    }
}
