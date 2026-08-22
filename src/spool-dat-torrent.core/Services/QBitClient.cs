using SpoolDatTorrent.Core.Configuration;
using SpoolDatTorrent.Core.DTOs;
using SpoolDatTorrent.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Text;

namespace SpoolDatTorrent.Core.Services
{
    public class QBitClient : IBitTorrentClient
    {
        private readonly HttpClient _httpClient;
        private readonly GlobalSpoolSettings _settings;
        private string? _cookie;

        public QBitClient(HttpClient httpClient, GlobalSpoolSettings settings)
        {
            _httpClient = httpClient;
            _settings = settings;

            if (_httpClient.BaseAddress == null && !string.IsNullOrEmpty(_settings.TorrentClientHost))
            {
                _httpClient.BaseAddress = new Uri(_settings.TorrentClientHost);
            }
        }

        public async Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default)
        {
            // qBittorrent Web API login endpoint
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("username", "admin"), // Can be mapped to settings if needed
                new KeyValuePair<string, string>("password", _settings.TorrentClientApiKey)
            });

            var response = await _httpClient.PostAsync("/api/v2/auth/login", content, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
                {
                    _cookie = string.Join("; ", cookies);
                }
                return true;
            }

            return false;
        }

        public async Task<long> GetActiveFootprintBytesAsync(string torrentId, CancellationToken cancellationToken = default)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v2/torrents/info?hashes={torrentId}");
            AddAuthHeader(request);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return 0;

            var torrents = await response.Content.ReadFromJsonAsync<TorrentInfoDto[]>(cancellationToken: cancellationToken);
            if (torrents != null && torrents.Length > 0)
            {
                // Return total downloaded size or completed size depending on tracking metric
                return torrents[0].Downloaded;
            }

            return 0;
        }

        public async Task<IReadOnlyList<TorrentFileDto>> GetFilesAsync(string torrentId, CancellationToken cancellationToken = default)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v2/torrents/files?hash={torrentId}");
            AddAuthHeader(request);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Array.Empty<TorrentFileDto>();
            }

            var files = await response.Content.ReadFromJsonAsync<List<TorrentFileDto>>(cancellationToken: cancellationToken);
            return files ?? (IReadOnlyList<TorrentFileDto>)Array.Empty<TorrentFileDto>();
        }

        public async Task MoveFilesAsync(string torrentId, string newDestinationPath, CancellationToken cancellationToken = default)
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("hashes", torrentId),
                new KeyValuePair<string, string>("location", newDestinationPath)
            });
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v2/torrents/setLocation") { Content = content };
            AddAuthHeader(request);

            await _httpClient.SendAsync(request, cancellationToken);
        }

        public async Task PauseTorrentAsync(string torrentId, CancellationToken cancellationToken = default)
        {
            var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("hashes", torrentId) });
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v2/torrents/pause") { Content = content };
            AddAuthHeader(request);

            await _httpClient.SendAsync(request, cancellationToken);
        }

        public async Task ResumeTorrentAsync(string torrentId, CancellationToken cancellationToken = default)
        {
            var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("hashes", torrentId) });
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v2/torrents/resume") { Content = content };
            AddAuthHeader(request);

            await _httpClient.SendAsync(request, cancellationToken);
        }

        public async Task SetDownloadLimitAsync(string torrentId, long bytesPerSecond, CancellationToken cancellationToken = default)
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("hashes", torrentId),
                new KeyValuePair<string, string>("limit", bytesPerSecond.ToString())
            });
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v2/torrents/downloadLimit") { Content = content };
            AddAuthHeader(request);

            await _httpClient.SendAsync(request, cancellationToken);
        }

        public async Task SetFilePrioritiesAsync(string torrentId, IEnumerable<int> fileIndices, int priority, CancellationToken cancellationToken = default)
        {
            var idList = string.Join("|", fileIndices);
            var content = new FormUrlEncodedContent(new[]
            {
        new KeyValuePair<string, string>("hash", torrentId),
        new KeyValuePair<string, string>("file_ids", idList),
        new KeyValuePair<string, string>("priority", priority.ToString())
    });

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v2/torrents/filePrio") { Content = content };
            AddAuthHeader(request);

            await _httpClient.SendAsync(request, cancellationToken);
        }
        private void AddAuthHeader(HttpRequestMessage request)
        {
            if (!string.IsNullOrEmpty(_cookie))
            {
                request.Headers.Add("Cookie", _cookie);
            }
        }
    }
}
