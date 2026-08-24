using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Spectre.Console;
using SpoolDatTorrent.Core.Configuration;
using SpoolDatTorrent.Core.Interfaces;
using SpoolDatTorrent.Core.Services;
using SpoolDatTorrent.Cli.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SpoolDatTorrent.Cli.Commands
{
    /// <summary>
    /// Shared spooling monitor loop used by both the "spool" (default) and "add" commands.
    /// Runs the engine in the background and renders a live Spectre Progress display.
    /// </summary>
    internal static class SpoolMonitor
    {
        public static async Task RunAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            // Install a Ctrl+C handler that prevents the OS from hard-killing the process
            // and instead cancels a linked token. This lets the loop exit gracefully so the
            // finally block (which restores the cursor) always runs.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };
            cancellationToken = cts.Token;

            var engine = serviceProvider.GetRequiredService<SpoolingEngine>();
            var reporter = (SpectreProgressReporter)serviceProvider.GetRequiredService<ISpoolingProgressReporter>();

            // Poll interval (seconds) drives how often the engine queries qBittorrent for
            // file progress. This is the granularity of the download % shown in the UI.
            var pollInterval = serviceProvider
                .GetRequiredService<IOptions<GlobalSpoolSettings>>()
                .Value.PollIntervalSeconds;

            // Run the engine loop in the background at the configured poll cadence.
            var engineTask = Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await engine.EvaluateAllStreamsAsync(cancellationToken);
                    await Task.Delay(TimeSpan.FromSeconds(pollInterval), cancellationToken);
                }
            }, cancellationToken);

            // Real Spectre Progress widget: the status message is shown as an indeterminate
            // task at the top, and the current batch's files are the progress tasks below.
            // Both live in this single live region, so both refresh once per second.
            try
            {
                await AnsiConsole.Progress()
                .AutoClear(false)
                .AutoRefresh(true)
                .HideCompleted(true)
                .Columns(new ProgressColumn[]
                {
                    new SpinnerColumn(Spinner.Known.Dots),
                    new RemainingTimeColumn(),
                    new PercentageColumn(),
                    new ProgressBarColumn() { Width = 30 },
                    new TaskDescriptionColumn() { Alignment = Justify.Left }
                })
                .StartAsync(async ctx =>
                {
                    // Spectre renders tasks in the order they are added, and that order is
                    // fixed. To get Jobs (top) -> Status (middle) -> Files (bottom), we add
                    // the status task lazily, only after the first job task exists.
                    ProgressTask? statusTask = null;

                    var tasks = new Dictionary<string, ProgressTask>(StringComparer.OrdinalIgnoreCase);
                    var jobTasks = new Dictionary<string, ProgressTask>(StringComparer.OrdinalIgnoreCase);

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        // 1. Job-level completion (moved / total desired files), one task per stream.
                        foreach (var stream in reporter.GetStreams())
                        {
                            var description = $"[yellow]({stream.StreamId}) {stream.Name} — {stream.MovedCount} / {stream.TotalCount} files processed[/]";
                            if (!jobTasks.TryGetValue(stream.TorrentIdentifier, out var jobTask))
                            {
                                jobTask = jobTasks[stream.TorrentIdentifier] = ctx.AddTask(description, maxValue: stream.TotalCount > 0 ? stream.TotalCount : 1);
                            }
                            jobTask.Description = description;
                            jobTask.MaxValue = stream.TotalCount > 0 ? stream.TotalCount : 1;
                            jobTask.Value = stream.MovedCount;
                        }

                        // Remove job tasks for streams no longer reported.
                        var activeJobs = new HashSet<string>(reporter.GetStreams().Select(s => s.TorrentIdentifier), StringComparer.OrdinalIgnoreCase);
                        foreach (var id in jobTasks.Keys.Where(k => !activeJobs.Contains(k)).ToList())
                        {
                            jobTasks[id].StopTask();
                            jobTasks.Remove(id);
                        }

                        // 2. Status line (middle) — created only after jobs exist so it sits below them.
                        if (statusTask == null && jobTasks.Count > 0)
                        {
                            statusTask = ctx.AddTask("Initialising...", maxValue: 100);
                            statusTask.IsIndeterminate();
                        }
                        if (statusTask != null)
                        {
                            statusTask.Description = reporter.GetStatus();
                        }

                        // 3. Files (bottom).
                        var files = reporter.GetFiles();

                        foreach (var file in files)
                        {
                            if (!tasks.TryGetValue(file.Name, out var task))
                            {
                                task = tasks[file.Name] = ctx.AddTask($"[Grey46]({file.StreamId}) {file.Name}[/]", maxValue: 100);
                            }
                            task.Value = Math.Clamp(file.Progress * 100, 0, 100);
                        }

                        // Remove tasks for files no longer in the batch.
                        var active = new HashSet<string>(files.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
                        foreach (var name in tasks.Keys.Where(k => !active.Contains(k)).ToList())
                        {
                            tasks[name].StopTask();
                            tasks.Remove(name);
                        }

                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    }
                });

            await engineTask;
            }
            finally
            {
                AnsiConsole.Cursor.Show();
            }
        }
    }
}
