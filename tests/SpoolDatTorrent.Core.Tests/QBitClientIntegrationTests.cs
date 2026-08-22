using SpoolDatTorrent.Core.Configuration;
using SpoolDatTorrent.Core.Services;
using System;
using System.Collections.Generic;
using System.Text;

namespace SpoolDatTorrent.Core.Tests
{
    // Note: This test requires a live qBittorrent instance running to pass.
    public class QBitClientIntegrationTests
    {
        [Fact]
        public async Task AuthenticateAsync_ValidCredentials_ReturnsTrue()
        {
            // Arrange
            var httpClient = new HttpClient();

            // UPDATE THESE to match your actual qBittorrent server!
            var profile = new TorrentServerProfile
            {
                Host = "http://localhost:8080",
                Username = "stigzler",
                Password = "astraastra",
                ApiKey = "qbt_4htwNXNVdgwaKpC92Z3UNAyGrwxV" // The client will prioritize this
            };

            var settings = new GlobalSpoolSettings();
            var qbitClient = new QBitClient(httpClient, settings, profile);

            // Act
            var isAuthenticated = await qbitClient.AuthenticateAsync();

            // Assert
            Assert.True(isAuthenticated, "Failed to authenticate. Double-check your Host IP/Port and ApiKey (password).");
        }

        [Fact]
        public async Task AddTorrentAsync_WithValidMagnet_SuccessfullyAddsToClient()
        {
            // Arrange
            var httpClient = new HttpClient();
            var profile = new TorrentServerProfile
            {
                Host = "http://localhost:8080",
                Username = "stigzler",
                Password = "astraastra", // Or leave blank if using your API key
                ApiKey = "qbt_4htwNXNVdgwaKpC92Z3UNAyGrwxV"
            };

            var settings = new GlobalSpoolSettings();
            var client = new QBitClient(httpClient, settings, profile);

            // Authenticate first
            var authenticated = await client.AuthenticateAsync();
            Assert.True(authenticated, "Authentication failed.");

            // Using a standard, reliable test magnet link (e.g., Ubuntu standard ISO)
            string testMagnet = "magnet:?xt=urn:btih:207399557a233b8dd49e8a71d79e9f9c7379d2fc&dn=ubuntu-22.04.3-desktop-amd64.iso";

            // Act & Assert - Should execute without throwing an exception
            var exception = await Record.ExceptionAsync(() => client.AddTorrentAsync(testMagnet));
            Assert.Null(exception);
        }

        [Fact]
        public async Task GetActiveFootprintBytesAsync_Authenticated_ReturnsNumericValue()
        {
            // Arrange
            var httpClient = new HttpClient();
            var profile = new TorrentServerProfile
            {
                Host = "http://localhost:8080",
                Username = "stigzler",
                Password = "astraastra",
                ApiKey = "qbt_4htwNXNVdgwaKpC92Z3UNAyGrwxV" // The client will prioritize this
            };

            var settings = new GlobalSpoolSettings();
            var qbitClient = new QBitClient(httpClient, settings, profile);

            // Act
            var isAuthenticated = await qbitClient.AuthenticateAsync();

            // 1. ASSERT AUTHENTICATION SUCCEEDS FIRST
            Assert.True(isAuthenticated, "Authentication failed! Cannot test footprint without valid access.");

            // 2. Fetch the footprint
            var bytes = await qbitClient.GetActiveFootprintBytesAsync("dummy_hash_12345");

            // Assert
            Assert.Equal(0, bytes);
        }
    }
}
