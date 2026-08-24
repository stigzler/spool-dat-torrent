using Spectre.Console.Cli;
using System.ComponentModel;

namespace SpoolDatTorrent.Cli.Commands
{
    public class ListStreamsSettings : CommandSettings
    {
        [CommandOption("-s|--status <STATUS>")]
        [Description("Optional filter by lifecycle status (Active, Paused, Completed, Error)")]
        public string? Status { get; set; }
    }
}
