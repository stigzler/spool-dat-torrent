using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace SpoolDatTorrent.Cli.Commands
{
    public class SpoolCommandSettings : CommandSettings
    {
        [CommandOption("-t|--torrent <PATH_OR_HASH>")]
        [Description("Path to the .torrent file, magnet link, or info-hash (Mandatory)")]
        public string? Torrent { get; set; }

        [CommandOption("-d|--dat <DAT_PATH>")]
        [Description("Path to the source DAT file (Mandatory)")]
        public string? DatPath { get; set; }

        [CommandOption("-n|--name <NAME>")]
        [Description("A friendly display name for the stream (used in logs/UI only; not part of the output path)")]
        public string? Name { get; set; }

        [CommandOption("--target <PATH>")]
        [Description("Destination folder. Files are placed directly into it (the torrent's shared root folder is stripped), preserving any subfolders below that root. Overrides the global default.")]
        public string? TargetOverride { get; set; }

        [CommandOption("-c|--cap <GIGABYTES>")]
        [Description("Override the spooling cap in GB for this run. A safety margin (see SpoolingCapSafetyMarginPercent) is reserved for BitTorrent boundary-piece overhead.")]
        public int? CapOverride { get; set; }

        [CommandOption("--strategy <STRATEGY>")]
        [Description("Post-completion behavior (e.g., MoveFiles, Pause)")]
        public string? Strategy { get; set; }

        [CommandOption("-f|--filter <PATTERN>")]
        [Description("File matching pattern (e.g., *USA*)")]
        public string? Filter { get; set; }

        [CommandOption("--client-host <URL>")]
        [Description("Override the BitTorrent client host URL")]
        public string? ClientHost { get; set; }

        [CommandOption("--client-key <KEY>")]
        [Description("Override the BitTorrent client API key")]
        public string? ClientKey { get; set; }

        [CommandOption("--fresh")]
        [Description("Start fresh: clear saved state and remove the torrent from the client, but keep files already moved to the destination")]
        public bool Fresh { get; set; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Torrent))
            {
                return ValidationResult.Error("You must provide a torrent path, magnet, or hash using -t or --torrent.");
            }

            if (string.IsNullOrWhiteSpace(DatPath))
            {
                return ValidationResult.Error("You must provide a DAT file path using -d or --dat.");
            }

            return ValidationResult.Success();
        }
    }
}