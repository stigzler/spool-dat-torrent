using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.Extensions.Options;
using SpoolDatTorrent.Core.Configuration;
using SpoolDatTorrent.Core.Services;
using Xunit;

namespace SpoolDatTorrent.Core.Tests
{
    public class BitTorrentClientFactoryTests
    {
        // A simple dummy factory so we don't have to install complex mocking frameworks yet
        private class DummyHttpClientFactory : IHttpClientFactory
        {
            public HttpClient CreateClient(string name) => new HttpClient();
        }

        [Fact]
        public void GetClient_ValidProfile_ReturnsConfiguredClient()
        {
            // Arrange
            var settings = new GlobalSpoolSettings
            {
                DefaultServerProfile = "LocalQBit",
                TorrentServers = new Dictionary<string, TorrentServerProfile>
                {
                    { "RemoteSeedbox", new TorrentServerProfile { Host = "http://seedbox:8080", ApiKey = "secret" } }
                }
            };
            var options = Options.Create(settings);
            var factory = new BitTorrentClientFactory(new DummyHttpClientFactory(), options);

            // Act
            var client = factory.GetClient("RemoteSeedbox");

            // Assert
            Assert.NotNull(client);
            Assert.IsType<QBitClient>(client);
        }

        [Fact]
        public void GetClient_MissingProfile_FallsBackToDefault()
        {
            // Arrange
            var settings = new GlobalSpoolSettings
            {
                DefaultServerProfile = "LocalQBit",
                TorrentServers = new Dictionary<string, TorrentServerProfile>
                {
                    { "LocalQBit", new TorrentServerProfile { Host = "http://localhost:8080" } }
                }
            };
            var options = Options.Create(settings);
            var factory = new BitTorrentClientFactory(new DummyHttpClientFactory(), options);

            // Act - Passing a null/empty string should trigger the fallback
            var client = factory.GetClient("");

            // Assert
            Assert.NotNull(client);
        }

        [Fact]
        public void GetClient_InvalidProfileAndInvalidDefault_ThrowsException()
        {
            // Arrange
            var settings = new GlobalSpoolSettings
            {
                DefaultServerProfile = "NonExistentDefault",
                TorrentServers = new Dictionary<string, TorrentServerProfile>() // Empty dictionary
            };
            var options = Options.Create(settings);
            var factory = new BitTorrentClientFactory(new DummyHttpClientFactory(), options);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => factory.GetClient("MissingProfile"));
        }
    }
}