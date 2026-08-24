using Spectre.Console;
using SpoolDatTorrent.Core.DTOs;
using System.Collections.Generic;

namespace SpoolDatTorrent.Cli.Commands
{
    /// <summary>
    /// Shared rendering of the streams table, used by both the "list" command and the
    /// "spool" command (which shows the list before starting the monitor).
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
    }
}
