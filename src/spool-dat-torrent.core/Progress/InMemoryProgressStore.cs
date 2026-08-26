using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using SpoolDatTorrent.Core.DTOs;

namespace SpoolDatTorrent.Core.Progress
{
    /// <summary>
    /// Thread-safe in-memory store of the latest stream progress snapshots and the latest
    /// global status message. Written by the engine (via a host implementation of
    /// <see cref="Interfaces.ISpoolingProgressReporter"/>) and read by host UIs (e.g. the
    /// web Streams page) on a polling cadence. Keeps live per-file progress without
    /// requiring a persistent DB write on every poll.
    /// </summary>
    public class InMemoryProgressStore : Interfaces.ISpoolingProgressReporter
    {
        private readonly object _lock = new();
        private readonly List<StreamProgressInfo> _streams = new();
        private string _status = string.Empty;

        public void ReportStreams(IReadOnlyList<StreamProgressInfo> streams)
        {
            lock (_lock)
            {
                _streams.Clear();
                _streams.AddRange(streams);
            }
        }

        public void ReportStatus(string message)
        {
            lock (_lock)
            {
                _status = message;
            }
        }

        /// <summary>Snapshot of the latest reported streams (ordered by created date).</summary>
        public IReadOnlyList<StreamProgressInfo> GetStreams()
        {
            lock (_lock)
            {
                return _streams
                    .OrderBy(s => s.CreatedUtc)
                    .ToList();
            }
        }

        /// <summary>Latest reported global status message.</summary>
        public string GetGlobalStatus()
        {
            lock (_lock)
            {
                return _status;
            }
        }

        /// <summary>Remove a stream (e.g. after it is cancelled/removed) from the store.</summary>
        public void Remove(string torrentIdentifier)
        {
            lock (_lock)
            {
                _streams.RemoveAll(s => s.TorrentIdentifier == torrentIdentifier);
            }
        }

        /// <summary>Update the status of the given stream IDs in the live snapshots.</summary>
        public void UpdateStatuses(IEnumerable<int> streamIds, string status)
        {
            var ids = new HashSet<int>(streamIds);
            lock (_lock)
            {
                foreach (var s in _streams)
                {
                    if (ids.Contains(s.StreamId))
                    {
                        s.Status = status;
                    }
                }
            }
        }

        /// <summary>Update the status of a single stream in the live snapshots.</summary>
        public void UpdateStatus(int streamId, string status)
        {
            lock (_lock)
            {
                foreach (var s in _streams)
                {
                    if (s.StreamId == streamId)
                    {
                        s.Status = status;
                    }
                }
            }
        }
    }
}
