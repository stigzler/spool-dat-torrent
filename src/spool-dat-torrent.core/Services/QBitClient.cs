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

        public async Task<string> GetTorrentSavePathAsync(string torrentId, CancellationToken cancellationToken = default)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v2/torrents/info?hashes={torrentId}");
            AddAuthHeader(request);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return string.Empty;

            // Dynamically parse the save_path without needing a dedicated DTO
            using var document = await System.Text.Json.JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var root = document.RootElement;

            if (root.ValueKind == System.Text.Json.JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                if (root[0].TryGetProperty("save_path", out var savePathElement))
                {
                    return savePathElement.GetString() ?? string.Empty;
                }
            }
            return string.Empty;
        }

        public async Task<string> GetTorrentNameAsync(string torrentId, CancellationToken cancellationToken = default)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v2/torrents/info?hashes={torrentId}");
            AddAuthHeader(request);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return string.Empty;

            using var document = await System.Text.Json.JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var root = document.RootElement;

            if (root.ValueKind == System.Text.Json.JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                if (root[0].TryGetProperty("name", out var nameElement))
                {
                    return nameElement.GetString() ?? string.Empty;
                }
            }
            return string.Empty;
        }

        public async Task<TorrentInfoDto?> GetTorrentInfoAsync(string torrentId, CancellationToken cancellationToken = default)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v2/torrents/info?hashes={torrentId}");
            AddAuthHeader(request);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var torrents = await response.Content.ReadFromJsonAsync<TorrentInfoDto[]>(cancellationToken: cancellationToken);
            return torrents is { Length: > 0 } ? torrents[0] : null;
        }

        public async Task AddTorrentAsync(string torrentPathOrMagnet, string? savePath = null, bool addPaused = true, CancellationToken cancellationToken = default)
        {
            using var content = new MultipartFormDataContent();

            // 1. ADD SETTINGS FIRST (qBittorrent ignores them if the file is parsed first)
            if (!string.IsNullOrEmpty(savePath))
            {
                content.Add(new StringContent(savePath), "savepath");
            }

            // Send both paused and stopped to ensure compatibility across qB 4.x and 5.x
            content.Add(new StringContent(addPaused ? "true" : "false"), "stopped");

            // 2. ADD FILE/URL SECOND
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

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v2/torrents/add")
            {
                Content = content
            };

            AddAuthHeader(request);

            var response = await _httpClient.SendAsync(request, cancellationToken);

            // 409 Conflict means the torrent (same info-hash) is already in the transfer
            // list. This is an expected, recoverable state during the delete/re-add cycle
            // (and on CLI re-runs), so treat it as a no-op rather than throwing.
            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                return;
            }

            response.EnsureSuccessStatusCode();
        }

        public async Task<long> GetPieceSizeAsync(string torrentId, CancellationToken cancellationToken = default)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v2/torrents/info?hashes={torrentId}");
            AddAuthHeader(request);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return 16 * 1024 * 1024; // Fallback to 16MB if it fails

            using var document = await System.Text.Json.JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var root = document.RootElement;

            if (root.ValueKind == System.Text.Json.JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                if (root[0].TryGetProperty("piece_size", out var pieceSizeElement))
                {
                    return pieceSizeElement.GetInt64();
                }
            }
            return 16 * 1024 * 1024;
        }

        public async Task RecheckTorrentAsync(string torrentId, CancellationToken cancellationToken = default)
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("hashes", torrentId)
            });

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v2/torrents/recheck") { Content = content };
            AddAuthHeader(request);

            await _httpClient.SendAsync(request, cancellationToken);
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
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v2/torrents/stop") { Content = content };
            AddAuthHeader(request);

            await _httpClient.SendAsync(request, cancellationToken);
        }

        public async Task ResumeTorrentAsync(string torrentId, CancellationToken cancellationToken = default)
        {
            var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("hashes", torrentId) });
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v2/torrents/start") { Content = content };
            AddAuthHeader(request);

            await _httpClient.SendAsync(request, cancellationToken);
        }

        public async Task DeleteTorrentAsync(string torrentId, bool deleteFiles, CancellationToken cancellationToken = default)
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("hashes", torrentId),
                new KeyValuePair<string, string>("deleteFiles", deleteFiles ? "true" : "false")
            });

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v2/torrents/delete") { Content = content };
            AddAuthHeader(request);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            // qBittorrent removes the torrent asynchronously. Poll until it is actually
            // gone so a subsequent re-add of the same info-hash doesn't hit a 409 Conflict.
            // Large torrents (multi-TB) can take a while to delete their files, so allow a
            // generous window (60s) before giving up.
            for (int i = 0; i < 60; i++)
            {
                if (!await TorrentExistsAsync(torrentId, cancellationToken))
                {
                    return;
                }
                await Task.Delay(1000, cancellationToken);
            }

            Console.WriteLine($"[Warning] Torrent '{torrentId}' still present after 60s; re-add may conflict.");
        }

        public async Task<bool> TorrentExistsAsync(string torrentId, CancellationToken cancellationToken = default)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v2/torrents/info?hashes={torrentId}");
            AddAuthHeader(request);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return false;

            using var document = await System.Text.Json.JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

            return document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array
                && document.RootElement.GetArrayLength() > 0;
        }

        public async Task<IReadOnlyList<string>> GetAllTorrentHashesAsync(CancellationToken cancellationToken = default)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v2/torrents/info");
            AddAuthHeader(request);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return Array.Empty<string>();

            using var document = await System.Text.Json.JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

            var hashes = new List<string>();
            if (document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var element in document.RootElement.EnumerateArray())
                {
                    if (element.TryGetProperty("hash", out var hashElement))
                    {
                        var hash = hashElement.GetString();
                        if (!string.IsNullOrWhiteSpace(hash))
                        {
                            hashes.Add(hash);
                        }
                    }
                }
            }

            return hashes;
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
