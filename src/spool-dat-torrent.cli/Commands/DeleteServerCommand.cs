using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using SpoolDatTorrent.Core.Configuration;
using SpoolDatTorrent.Core.Data;
using SpoolDatTorrent.Core.Helpers;
using System;
using System.Linq;
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

            // Determine whether the profile being deleted is the configured default.
            var defaultProfile = serviceProvider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<GlobalSpoolSettings>>()
                .Value.DefaultServerProfile;
            var isDefault = string.Equals(defaultProfile, settings.Name, StringComparison.OrdinalIgnoreCase);

            // Refuse to delete a profile that is still referenced by one or more streams.
            // This includes streams that explicitly reference the profile AND streams with
            // no explicit profile that would resolve to this profile via the default.
            using (var scope = serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<SpoolDbContext>();
                var referencing = await db.Streams
                    .Where(s => s.ServerProfileId == settings.Name ||
                                (isDefault && (s.ServerProfileId == null || s.ServerProfileId == string.Empty)))
                    .Select(s => s.Name)
                    .ToListAsync(cancellationToken);

                if (referencing.Count > 0)
                {
                    var plainNames = string.Join(", ", referencing);
                    var error = $"Cannot delete server profile '{settings.Name}' because it is assigned to the following stream(s): {plainNames}. Cancel those streams first.";

                    Logger.Log($"[Error] {error}");
                    AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
                    return 1;
                }
            }

            var removed = SettingsManager.DeleteServerProfile(settings.Name!);

            if (removed)
            {
                AnsiConsole.MarkupLine($"[green]Deleted server profile:[/] {Markup.Escape(settings.Name!)}");
                Logger.Log($"Deleted server profile: {settings.Name}");
            }
            else
            {
                var error = $"Server profile not found: {settings.Name}";
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
                Logger.Log($"[Delete] {error}");
                return 1;
            }

            return 0;
        }
    }
}
