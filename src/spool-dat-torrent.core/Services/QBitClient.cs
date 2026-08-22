using SpoolDatTorrent.Core.Configuration;
using SpoolDatTorrent.Core.DTOs;
using SpoolDatTorrent.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace SpoolDatTorrent.Core.Services
{
    public class QBitClient : IBitTorrentClient
    {
        private readonly HttpClient _httpClient;
        private readonly TorrentServerProfile _profile;
        private readonly GlobalSpoolSettings _settings;
        private readonly bool _useApiKey;
        private string? _cookie;
        public QBitClient(HttpClient httpClient, GlobalSpoolSettings settings, TorrentServerProfile profile)
        {
            _httpClient = httpClient;
            _settings = settings;
            _profile = profile;

            if (_httpClient.BaseAddress == null && !string.IsNullOrEmpty(_profile.Host))
            {
                _httpClient.BaseAddress = new Uri(_profile.Host);
            }

            // Check if we are using the modern API Key auth
            if (!string.IsNullOrWhiteSpace(_profile.ApiKey))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _profile.ApiKey);
                _useApiKey = true;
            }
        }
        public async Task AddTorrentAsync(string torrentPathOrMagnet, string? savePath = null, CancellationToken cancellationToken = default)
        {
            using var content = new MultipartFormDataContent();

            // Check if it's a physical .torrent file
            if (File.Exists(torrentPathOrMagnet) && torrentPathOrMagnet.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
            {
                var fileBytes = await File.ReadAllBytesAsync(torrentPathOrMagnet, cancellationToken);
                var fileContent = new ByteArrayContent(fileBytes);
                content.Add(fileContent, "torrents", Path.GetFileName(torrentPathOrMagnet));
            }
            else // Otherwise, treat it as a Magnet link or URL
            {
                content.Add(new StringContent(torrentPathOrMagnet), "urls");
            }

            if (!string.IsNullOrEmpty(savePath))
            {
                content.Add(new StringContent(savePath), "savepath");
            }

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v2/torrents/add")
            {
                Content = content
            };

            AddAuthHeader(request);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }

        public async Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default)
        {
            // If using the API key, ping a lightweight endpoint to verify the key is actually valid
            if (_useApiKey)
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "/api/v2/app/version");
                var response = await _httpClient.SendAsync(request, cancellationToken);

                // If we get a 200 OK, the API key works. If we get a 403 Forbidden, it's invalid.
                return response.IsSuccessStatusCode;
            }

            // Fallback for older cookie-based authentication
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("username", _profile.Username),
                new KeyValuePair<string, string>("password", _profile.Password)
            });

            var authResponse = await _httpClient.PostAsync("/api/v2/auth/login", content, cancellationToken);
            if (authResponse.IsSuccessStatusCode)
            {
                if (authResponse.Headers.TryGetValues("Set-Cookie", out var cookies))
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
                new KeyValuePair<string, string>("id", idList),
                new KeyValuePair<string, string>("priority", priority.ToString())
            });

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v2/torrents/filePrio") { Content = content };
            AddAuthHeader(request);

            await _httpClient.SendAsync(request, cancellationToken);
        }
        private void AddAuthHeader(HttpRequestMessage request)
        {
            // Only add the cookie if we aren't using the API Key
            if (!_useApiKey && !string.IsNullOrEmpty(_cookie))
            {
                request.Headers.Add("Cookie", _cookie);
            }
        }
    }
}
