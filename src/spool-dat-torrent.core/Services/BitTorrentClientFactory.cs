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

            // If the resolved profile (including DefaultServerProfile) still doesn't exist,
            // fall back to the first configured profile rather than throwing.
            if (!_settings.TorrentServers.TryGetValue(profileName, out var profile))
            {
                var firstProfile = _settings.TorrentServers.Keys.FirstOrDefault();
                if (firstProfile == null)
                {
                    throw new InvalidOperationException("No BitTorrent server profiles are configured.");
                }

                profileName = firstProfile;
                profile = _settings.TorrentServers[firstProfile];
            }

            // In the future, if profile.ClientType == BitTorrentClientType.Deluge, you return a DelugeClient here
            var httpClient = _httpClientFactory.CreateClient($"qbit_{profileName}");
            httpClient.BaseAddress = new Uri(profile.Host);

            return new QBitClient(httpClient, _settings, profile);
        }
    }
}
