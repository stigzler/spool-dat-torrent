using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpoolDatTorrent.Core.DTOs;

namespace SpoolDatTorrent.Core.Interfaces
{
    public interface IBitTorrentClient
    {
        Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default);
        Task<long> GetActiveFootprintBytesAsync(string torrentId, CancellationToken cancellationToken = default);
        Task PauseTorrentAsync(string torrentId, CancellationToken cancellationToken = default);
        Task ResumeTorrentAsync(string torrentId, CancellationToken cancellationToken = default);
        Task DeleteTorrentAsync(string torrentId, bool deleteFiles, CancellationToken cancellationToken = default);
        Task SetDownloadLimitAsync(string torrentId, long bytesPerSecond, CancellationToken cancellationToken = default);
        Task MoveFilesAsync(string torrentId, string newDestinationPath, CancellationToken cancellationToken = default);
        Task AddTorrentAsync(string torrentUrlOrMagnet, string? savePath = null, bool addPaused = true, CancellationToken cancellationToken = default);

        Task<long> GetPieceSizeAsync(string torrentId, CancellationToken cancellationToken = default);

        Task RecheckTorrentAsync(string torrentId, CancellationToken cancellationToken = default);

        // File-level control for selective batching & manual overrides
        Task<IReadOnlyList<TorrentFileDto>> GetFilesAsync(string torrentId, CancellationToken cancellationToken = default);
        Task SetFilePrioritiesAsync(string torrentId, IEnumerable<int> fileIndices, int priority, CancellationToken cancellationToken = default);

        Task<string> GetTorrentSavePathAsync(string torrentId, CancellationToken cancellationToken = default);
        Task<string> GetTorrentContentPathAsync(string torrentId, CancellationToken cancellationToken = default);
        Task<string> GetTorrentNameAsync(string torrentId, CancellationToken cancellationToken = default);
        Task<TorrentInfoDto?> GetTorrentInfoAsync(string torrentId, CancellationToken cancellationToken = default);
        Task<bool> TorrentExistsAsync(string torrentId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<string>> GetAllTorrentHashesAsync(CancellationToken cancellationToken = default);
    }
}