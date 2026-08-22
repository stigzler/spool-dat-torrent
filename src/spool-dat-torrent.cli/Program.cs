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
            });

            return await app.RunAsync(args);
        }
    }
}