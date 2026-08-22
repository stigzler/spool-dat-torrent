using Microsoft.Extensions.Options;
using SpoolDatTorrent.Core.Configuration;
using SpoolDatTorrent.Core.Interfaces;
using System.Net.Http;
using System;
using System.Collections.Generic;
using System.Text;

namespace SpoolDatTorrent.Core.Services
{
    public class BitTorrentClientFactory: IBitTorrentClientFactory
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly GlobalSpoolSettings _settings;

        public BitTorrentClientFactory(IHttpClientFactory httpClientFactory, IOptions<GlobalSpoolSettings> settings)
        {
            _httpClientFactory = httpClientFactory;
            _settings = settings.Value;
        }

        public IBitTorrentClient GetClient(string profileName)
        {
            // Fall back to default if the stream's profile name is empty or missing
            if (string.IsNullOrWhiteSpace(profileName) || !_settings.TorrentServers.ContainsKey(profileName))
            {
                profileName = _settings.DefaultServerProfile;
            }

            if (!_settings.TorrentServers.TryGetValue(profileName, out var profile))
            {
                throw new InvalidOperationException($"Server profile '{profileName}' not found in configuration.");
            }

            // In the future, if profile.ClientType == "Deluge", you return a DelugeClient here
            var httpClient = _httpClientFactory.CreateClient($"qbit_{profileName}");
            httpClient.BaseAddress = new Uri(profile.Host);

            return new QBitClient(httpClient, _settings, profile);
        }
    }
}
