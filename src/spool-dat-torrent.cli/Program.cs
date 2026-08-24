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
            var app = new CommandApp<RunEngineCommand>();

            app.Configure(config =>
            {
                config.SetApplicationName("spool");

                config.AddCommand<CancelStreamCommand>("cancel")
                    .WithDescription("Cancel a single stream (remove its torrent and delete the stream row).");

                config.AddCommand<CancelAllStreamsCommand>("cancel-all")
                    .WithDescription("Cancel all streams (remove every torrent and clear all stream rows).");

                config.AddCommand<ListStreamsCommand>("list")
                    .WithDescription("List all streams tracked in the database.");
            });

            return await app.RunAsync(args);
        }
    }
}