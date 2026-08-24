using Spectre.Console;
using Spectre.Console.Cli;
using SpoolDatTorrent.Core.Configuration;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SpoolDatTorrent.Cli.Commands
{
    /// <summary>
    /// Opens the spool_settings.json file in the system's default text editor.
    /// </summary>
    public class EditConfigCommand : AsyncCommand
    {
        protected override Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
        {
            var configPath = SettingsManager.GetSettingsPath();

            if (!File.Exists(configPath))
            {
                // Ensure the file exists before trying to open it.
                SettingsManager.EnsureDefaultSettingsExist();
            }

            AnsiConsole.MarkupLine($"[green]Opening config:[/] {Markup.Escape(configPath)}");

            var startInfo = new ProcessStartInfo(configPath)
            {
                UseShellExecute = true
            };

            try
            {
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Could not open editor:[/] {Markup.Escape(ex.Message)}");
                AnsiConsole.MarkupLine($"[grey]Open the file manually at:[/] {Markup.Escape(configPath)}");
                return Task.FromResult(1);
            }

            return Task.FromResult(0);
        }
    }
}
