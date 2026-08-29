using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SpoolDatTorrent.Core.Configuration;
using SpoolDatTorrent.Core.Data;
using SpoolDatTorrent.Core.Helpers;
using SpoolDatTorrent.Core.Interfaces;
using SpoolDatTorrent.Core.Models;

namespace SpoolDatTorrent.Core.Commands
{
    /// <summary>
    /// Edits mutable aspects of an existing stream. Reusable by the CLI, Docker web UI, and
    /// desktop apps. The edit dialog sends the complete desired state of all editable
    /// properties, so every field is always applied (no "unchanged" sentinel needed).
    /// </summary>
    public class EditStreamCommand
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly GlobalSpoolSettings _settings;
        private readonly IBitTorrentClientFactory _clientFactory;

        public EditStreamCommand(
            IServiceScopeFactory scopeFactory,
            IOptions<GlobalSpoolSettings> settings,
            IBitTorrentClientFactory clientFactory)
        {
            _scopeFactory = scopeFactory;
            _settings = settings.Value;
            _clientFactory = clientFactory;
        }

        /// <summary>
        /// Edit a stream by its numeric database Id.
        /// </summary>
        /// <returns>A result describing success or a validation error.</returns>
        public async Task<EditStreamResult> ExecuteByIdAsync(
            int streamId,
            EditStreamRequest request,
            CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SpoolDbContext>();
            var stream = await db.Streams.FirstOrDefaultAsync(s => s.Id == streamId, cancellationToken);

            if (stream == null)
            {
                return new EditStreamResult { Success = false, Error = "Stream not found." };
            }

            return await ApplyAsync(db, stream, request, cancellationToken);
        }

        private async Task<EditStreamResult> ApplyAsync(
            SpoolDbContext db,
            TorrentStreamItem stream,
            EditStreamRequest request,
            CancellationToken cancellationToken)
        {
            // Validate the cap against the stream's server profile (resolving the default
            // profile when the stream has none).
            if (request.SpoolingCapGb.HasValue)
            {
                string profileName = string.IsNullOrWhiteSpace(stream.ServerProfileId)
                    ? _settings.DefaultServerProfile
                    : stream.ServerProfileId;

                if (!_settings.TorrentServers.TryGetValue(profileName, out var profile))
                {
                    return new EditStreamResult { Success = false, Error = $"Server profile '{profileName}' does not exist." };
                }

                if (request.SpoolingCapGb.Value <= 0)
                {
                    return new EditStreamResult { Success = false, Error = "Spooling cap must be greater than 0 GB." };
                }

                if (request.SpoolingCapGb.Value > profile.SpoolingCapGb)
                {
                    return new EditStreamResult
                    {
                        Success = false,
                        Error = $"Spooling cap cannot exceed the server profile's cap of {profile.SpoolingCapGb} GB."
                    };
                }
            }

            if (request.SettlingTimeSeconds.HasValue && request.SettlingTimeSeconds.Value < 1)
            {
                return new EditStreamResult { Success = false, Error = "Settling time must be at least 1 second." };
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return new EditStreamResult { Success = false, Error = "Stream name cannot be empty." };
            }

            stream.Name = request.Name.Trim();
            stream.Strategy = request.Strategy;
            stream.SettlingTimeSeconds = request.SettlingTimeSeconds;
            stream.PriorityTerms = request.PriorityTerms ?? string.Empty;
            stream.DePriorityTerms = request.DePriorityTerms ?? string.Empty;
            stream.SpoolingCapGb = request.SpoolingCapGb;

            // Changing the strategy away from RateLimit means the engine will clear the
            // download limit on the next allocation, so drop the persisted "Rate Limited"
            // indicator now (it would otherwise linger until the next poll).
            if (request.Strategy != SpoolingStrategy.RateLimit)
            {
                stream.IsRateLimited = false;
            }

            await db.SaveChangesAsync(cancellationToken);

            // If the strategy changed away from RateLimit, immediately lift the download
            // limit in the BitTorrent client (rather than waiting for the next engine poll).
            if (request.Strategy != SpoolingStrategy.RateLimit)
            {
                await ClearRateLimitInClientAsync(stream, cancellationToken);
            }

            Logger.Log($"✏️ Updated stream '{stream.Name}': strategy={stream.Strategy}, " +
                       $"settling={stream.SettlingTimeSeconds?.ToString() ?? "default"}s, " +
                       $"priorityTerms='{stream.PriorityTerms}', dePriorityTerms='{stream.DePriorityTerms}', " +
                       $"spoolingCapGb={stream.SpoolingCapGb?.ToString() ?? "(default)"}.");
            return new EditStreamResult { Success = true };
        }

        /// <summary>
        /// Immediately clears the download rate limit for a stream in the BitTorrent client
        /// (-1 = unlimited). Best-effort: failures are logged but do not fail the edit.
        /// </summary>
        private async Task ClearRateLimitInClientAsync(TorrentStreamItem stream, CancellationToken cancellationToken)
        {
            try
            {
                string profileName = string.IsNullOrWhiteSpace(stream.ServerProfileId)
                    ? _settings.DefaultServerProfile
                    : stream.ServerProfileId;

                var client = _clientFactory.GetClient(profileName);
                await client.AuthenticateAsync(cancellationToken);
                await client.SetDownloadLimitAsync(stream.TorrentIdentifier, -1, cancellationToken);
                Logger.Log($"🔓 Cleared rate limit for stream '{stream.Name}' in the BitTorrent client.");
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Could not clear rate limit for stream '{stream.Name}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// The complete desired state of a stream's editable properties, sent by the edit dialog.
    /// </summary>
    public class EditStreamRequest
    {
        public string Name { get; set; } = string.Empty;
        public SpoolingStrategy Strategy { get; set; } = SpoolingStrategy.MoveFiles;

        /// <summary>Per-stream settling time (seconds). Null uses the global default.</summary>
        public int? SettlingTimeSeconds { get; set; }

        public string PriorityTerms { get; set; } = string.Empty;
        public string DePriorityTerms { get; set; } = string.Empty;

        /// <summary>Per-stream spooling cap (GB). Null clears the override (fair-share split).</summary>
        public long? SpoolingCapGb { get; set; }
    }

    /// <summary>
    /// Result of an <see cref="EditStreamCommand"/> operation.
    /// </summary>
    public class EditStreamResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
    }
}
