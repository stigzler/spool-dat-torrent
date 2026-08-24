using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace SpoolDatTorrent.Cli.Commands
{
    public class DeleteServerSettings : CommandSettings
    {
        [CommandArgument(0, "<NAME>")]
        [Description("Name of the server profile to delete (Mandatory)")]
        public string? Name { get; set; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                return ValidationResult.Error("You must provide the name of the server profile to delete.");
            }

            return ValidationResult.Success();
        }
    }
}
