using System;
using System.Collections.Generic;
using System.Linq;
using Spectre.Console;
using Spectre.Console.Rendering;
using SpoolDatTorrent.Core.DTOs;
using SpoolDatTorrent.Core.Interfaces;

namespace SpoolDatTorrent.Cli.Services
{
    /// <summary>
    /// Spectre.Console implementation of <see cref="ISpoolingProgressReporter"/>. Stores the
    /// latest progress state. The CLI renders it as: a boxed Jobs panel and Status panel
    /// (printed above), plus the real Spectre <see cref="AnsiConsole.Progress"/> widget for
    /// the current batch's files.
    /// </summary>
    public class SpectreProgressReporter : ISpoolingProgressReporter
    {
        private readonly object _lock = new();
        private IReadOnlyList<StreamProgressInfo> _streams = new List<StreamProgressInfo>();
        private string _status = "Waiting for first evaluation...";

        public void ReportStreams(IReadOnlyList<StreamProgressInfo> streams)
        {
            lock (_lock)
            {
                _streams = streams;
            }
        }

        public void ReportStatus(string message)
        {
            lock (_lock)
            {
                _status = message;
            }
        }

        /// <summary>Snapshot of the current streams (for the CLI to render the Jobs panel).</summary>
        public IReadOnlyList<StreamProgressInfo> GetStreams()
        {
            lock (_lock)
            {
                return _streams;
            }
        }

        /// <summary>Snapshot of the current batch files (for the CLI to drive the Progress widget).</summary>
        public IReadOnlyList<FileProgressInfo> GetFiles()
        {
            lock (_lock)
            {
                return _streams.SelectMany(s => s.Files).ToList();
            }
        }

        /// <summary>Snapshot of the latest status message.</summary>
        public string GetStatus()
        {
            lock (_lock)
            {
                return _status;
            }
        }

        /// <summary>Render the Status + Jobs sections as a static block (printed above the Progress widget).</summary>
        public IRenderable RenderHeader()
        {
            IReadOnlyList<StreamProgressInfo> streams;
            string status;
            lock (_lock)
            {
                streams = _streams;
                status = _status;
            }

            return new Rows(new List<IRenderable>
            {
                BuildStatusPanel(status),
                new Text(string.Empty),
                BuildJobsPanel(streams)
            });
        }

        private static IRenderable BuildStatusPanel(string status)
        {
            return new Panel(Markup.Escape(status))
            {
                Border = BoxBorder.Rounded,
                Header = new PanelHeader("Status"),
                Padding = new Padding(1, 0)
            };
        }

        private static IRenderable BuildJobsPanel(IReadOnlyList<StreamProgressInfo> streams)
        {
            var content = new List<IRenderable>();

            if (streams.Count == 0)
            {
                content.Add(new Markup("[grey]No jobs.[/]"));
            }
            else
            {
                foreach (var stream in streams)
                {
                    var statusColor = stream.Status switch
                    {
                        "Active" => "green",
                        "Completed" => "blue",
                        "Error" => "red",
                        _ => "grey"
                    };

                    content.Add(new Markup(
                        $"[bold]{Markup.Escape(stream.Name)}[/] [{statusColor}]{stream.Status}[/] [grey]({stream.MovedCount}/{stream.TotalCount} files)[/]"));
                    content.Add(new Markup($"  {RenderBar(stream.Progress)} {stream.Progress:P0}"));
                }
            }

            return new Panel(new Rows(content))
            {
                Border = BoxBorder.Rounded,
                Header = new PanelHeader("Jobs"),
                Padding = new Padding(1, 0)
            };
        }

        private static string RenderBar(double progress)
        {
            const int width = 20;
            int filled = (int)(progress * width);
            if (filled < 0) filled = 0;
            if (filled > width) filled = width;

            var color = progress >= 1.0 ? "green" : "yellow";
            return $"[{color}]{new string('█', filled)}[/][grey]{new string('░', width - filled)}[/]";
        }
    }
}
