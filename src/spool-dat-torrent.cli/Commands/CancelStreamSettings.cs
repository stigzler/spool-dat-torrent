using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace SpoolDatTorrent.Cli.Commands
{
    public class CancelStreamSettings : CommandSettings
    {
        [CommandArgument(0, "<STREAM_ID_OR_HASH>")]
        [Description("Stream Id, or path/magnet/info-hash of the stream to cancel (Mandatory)")]
        public string? Identifier { get; set; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Identifier))
            {
                return ValidationResult.Error("You must provide a stream Id, or a torrent path, magnet, or hash.");
            }

            return ValidationResult.Success();
        }
    }
}
