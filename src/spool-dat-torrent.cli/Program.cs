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
            // Fail-fast: log any unhandled exception (with full stack trace) to the log file,
            // then let the process exit. The CLI is a short-lived invocation, so a fatal error
            // should surface clearly rather than being swallowed.
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                Logger.LogError($"Unhandled exception (terminating): {(ex?.Message ?? "unknown")}");
                if (ex != null)
                {
                    Logger.LogError(ex.StackTrace ?? "(no stack trace)");
                }
            };

            // A fire-and-forget task that throws would otherwise silently kill the process.
            // Log it and mark it observed so the app can continue.
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Logger.LogError($"Unobserved task exception: {e.Exception.Message}");
                Logger.LogError(e.Exception.StackTrace ?? "(no stack trace)");
                e.SetObserved();
            };

            // Spectre.Console owns the console; raw logger writes would corrupt its live
            // display, so the logger writes to the file only in the CLI.
            Logger.EchoToConsole = false;

            // 1. Trigger config creation if missing
            SettingsManager.EnsureDefaultSettingsExist();

            // 2. Hand over execution to Spectre.Console
            var app = new CommandApp();

            app.Configure(config =>
            {
                config.SetApplicationName("SpoolDatTorrent");

                config.AddCommand<ListStreamsCommand>("list")
                    .WithDescription("List all servers + streams. OPTIONAL: -s|--status <Active|Paused|Completed|Error>.");

                config.AddCommand<RunEngineCommand>("spool")
                    .WithDescription("Start the spooling daemon for all active streams.");

                config.AddCommand<AddStreamCommand>("add")
                    .WithDescription("Add a stream + start spooling. REQUIRED: -t|--torrent <path|magnet|hash> and -d|--dat <path>. OPTIONAL: --server <name>.");                
                
                config.AddCommand<PauseStreamCommand>("pause")
                    .WithDescription("Pause a stream (stop spooling it). REQUIRED: <stream-id> or <path|magnet|hash>.");

                config.AddCommand<ResumeStreamCommand>("resume")
                    .WithDescription("Resume a paused stream. REQUIRED: <stream-id> or <path|magnet|hash>.");

                config.AddCommand<RetryStreamCommand>("retry")
                    .WithDescription("Re-activate an errored stream for retry. REQUIRED: <stream-id> or <path|magnet|hash>.");

                config.AddCommand<CancelStreamCommand>("cancel")
                    .WithDescription("Cancel a single stream. REQUIRED: <stream-id> or <path|magnet|hash>).");

                config.AddCommand<CancelAllStreamsCommand>("cancel-all")
                    .WithDescription("Cancel all streams (remove every torrent and clear all stream rows).");

                config.AddCommand<AddServerCommand>("add-server")
                    .WithDescription("Create a new BitTorrent server profile in the settings file.");

                config.AddCommand<DeleteServerCommand>("delete-server")
                    .WithDescription("Delete a BitTorrent server profile by name. REQUIRED: <name>.");

                config.AddCommand<EditConfigCommand>("config")
                    .WithDescription("Open the config file in the system's default text editor.");

                // Show how to get command-specific help in the top-level help output.
                config.AddExample(new[] { "add", "-h" ,": Show extended help for add"});
                config.AddExample(new[] { "cancel", "-h", ": Show extended help for cancel" });
                config.AddExample(new[] { "list", "-h", ": Show extended help for list" });
            });

            return await app.RunAsync(args);
        }
    }
}