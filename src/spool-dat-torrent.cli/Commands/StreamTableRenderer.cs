using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Spectre.Console;
using SpoolDatTorrent.Core.Configuration;
using SpoolDatTorrent.Core.DTOs;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SpoolDatTorrent.Cli.Commands
{
    /// <summary>
    /// Shared rendering of the streams table, used by the "list", "spool", and "add"
    /// commands (which show the list before starting the monitor).
    /// </summary>
    internal static class StreamTableRenderer
    {
        public static void Render(IReadOnlyList<StreamDetails> streams)
        {
            if (streams.Count == 0)
            {
                AnsiConsole.MarkupLine("[grey]No streams found.[/]");
                return;
            }

            var table = new Table();
            table.AddColumn("Id");
            table.AddColumn("Name");
            table.AddColumn("Status");
            table.AddColumn("Progress");
            table.AddColumn("Server");
            table.AddColumn("Created (UTC)");

            foreach (var s in streams)
            {
                var statusColor = s.Status switch
                {
                    "Active" => "green",
                    "Completed" => "blue",
                    "Error" => "red",
                    _ => "grey"
                };

                table.AddRow(
                    s.Id.ToString(),
                    Markup.Escape(s.Name),
                    $"[{statusColor}]{s.Status}[/]",
                    $"{s.MovedCount}/{s.TotalCount} ({s.Progress:P0})",
                    Markup.Escape(s.ServerProfileId),
                    s.CreatedUtc.ToString("yyyy-MM-dd HH:mm:ss"));
            }

            AnsiConsole.Write(table);
        }

        /// <summary>
        /// Render the server profiles table and the streams table (with headings), mirroring
        /// what the "list" command outputs. Used by "spool" and "add" before the monitor.
        /// </summary>
        public static async Task ShowServersAndStreamsAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            // Server profiles.
            AnsiConsole.MarkupLine("[bold]BitTorrent Server Profiles[/]");
            var serversCommand = new Core.Commands.ListServerProfilesCommand(
                serviceProvider.GetRequiredService<IOptions<GlobalSpoolSettings>>());
            var servers = await serversCommand.ExecuteAsync(cancellationToken);
            ServerTableRenderer.Render(servers);

            // Streams.
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Current Spooling Streams[/]");
            var streamsCommand = new Core.Commands.ListStreamsCommand(
                serviceProvider.GetRequiredService<IServiceScopeFactory>());
            var streams = await streamsCommand.ExecuteAsync(cancellationToken: cancellationToken);
            Render(streams);
        }
    }
}
