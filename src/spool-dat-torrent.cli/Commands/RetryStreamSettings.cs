using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace SpoolDatTorrent.Cli.Commands
{
    public class RetryStreamSettings : CommandSettings
    {
        [CommandArgument(0, "<PATH_OR_HASH>")]
        [Description("Path to the .torrent file, magnet link, or info-hash of the stream to retry (Mandatory)")]
        public string? Torrent { get; set; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Torrent))
            {
                return ValidationResult.Error("You must provide a torrent path, magnet, or hash.");
            }

            return ValidationResult.Success();
        }
    }
}
