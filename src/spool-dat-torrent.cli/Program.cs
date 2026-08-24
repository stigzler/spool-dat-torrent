using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;
using SpoolDatTorrent.Cli.Commands;
using SpoolDatTorrent.Core.Configuration;
using SpoolDatTorrent.Core.Data;
using SpoolDatTorrent.Core.Helpers;
using SpoolDatTorrent.Core.Interfaces;
using SpoolDatTorrent.Core.Models;
using SpoolDatTorrent.Core.Services;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SpoolDatTorrent.Cli
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {
            // 1. Trigger config creation if missing
            SettingsManager.EnsureDefaultSettingsExist();

            // 2. Hand over execution to Spectre.Console
            var app = new CommandApp();

            app.Configure(config =>
            {
                config.SetApplicationName("SpoolDatTorrent");

                config.AddCommand<RunEngineCommand>("spool")
                    .WithDescription("Start the spooling monitor for all active streams.");

                config.AddCommand<AddStreamCommand>("add")
                    .WithDescription("Add a stream and start spooling. REQUIRED: -t|--torrent <path|magnet|hash> and -d|--dat <path>.");

                config.AddCommand<CancelStreamCommand>("cancel")
                    .WithDescription("Cancel a single stream. REQUIRED: <path|magnet|hash> (positional).");

                config.AddCommand<CancelAllStreamsCommand>("cancel-all")
                    .WithDescription("Cancel all streams (remove every torrent and clear all stream rows).");

                config.AddCommand<ListStreamsCommand>("list")
                    .WithDescription("List all streams. OPTIONAL: -s|--status <Active|Paused|Completed|Error>.");

                // Show how to get command-specific help in the top-level help output.
                config.AddExample(new[] { "add", "-h" ,": Show extended help for add"});
                config.AddExample(new[] { "cancel", "-h", ": Show extended help for cancel" });
                config.AddExample(new[] { "list", "-h", ": Show extended help for list" });
            });

            return await app.RunAsync(args);
        }
    }
}