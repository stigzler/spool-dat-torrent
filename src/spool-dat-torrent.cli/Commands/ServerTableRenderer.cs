using Spectre.Console;
using SpoolDatTorrent.Core.DTOs;
using System.Collections.Generic;

namespace SpoolDatTorrent.Cli.Commands
{
    /// <summary>
    /// Shared rendering of the BitTorrent server profiles table, used by the "list" and
    /// "spool" commands.
    /// </summary>
    internal static class ServerTableRenderer
    {
        public static void Render(IReadOnlyList<ServerProfileDetails> servers)
        {
            if (servers.Count == 0)
            {
                AnsiConsole.MarkupLine("[grey]No server profiles configured.[/]");
                return;
            }

            var table = new Table();
            table.AddColumn("Name");
            table.AddColumn("ClientType");
            table.AddColumn("Host");
            table.AddColumn("Username");
            table.AddColumn("ApiKey");
            table.AddColumn("Cap (GB)");

            foreach (var s in servers)
            {
                table.AddRow(
                    Markup.Escape(s.Name),
                    Markup.Escape(s.ClientType),
                    Markup.Escape(s.Host),
                    Markup.Escape(s.Username),
                    s.HasApiKey ? "[green]yes[/]" : "[grey]no[/]",
                    s.SpoolingCapGb.ToString());
            }

            AnsiConsole.Write(table);
        }
    }
}
