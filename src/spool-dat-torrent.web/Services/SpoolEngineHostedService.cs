using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpoolDatTorrent.Core.Configuration;
using SpoolDatTorrent.Core.Progress;
using SpoolDatTorrent.Core.Services;

namespace SpoolDatTorrent.Web.Services
{
    /// <summary>
    /// Runs the spooling engine on a background loop inside the web host at the configured
    /// poll cadence. Progress snapshots are routed to the shared
    /// <see cref="InMemoryProgressStore"/> so the Streams page can render live progress.
    /// </summary>
    public class SpoolEngineHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly InMemoryProgressStore _store;
        private readonly ILogger<SpoolEngineHostedService> _logger;

        public SpoolEngineHostedService(
            IServiceScopeFactory scopeFactory,
            InMemoryProgressStore store,
            IOptions<GlobalSpoolSettings> settings,
            ILogger<SpoolEngineHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _store = store;
            _logger = logger;
            PollIntervalSeconds = settings.Value.PollIntervalSeconds;
        }

        private int PollIntervalSeconds { get; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Give the app a moment to fully start before the first engine pass.
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var engine = scope.ServiceProvider.GetRequiredService<SpoolingEngine>();
                    await engine.EvaluateAllStreamsAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Normal shutdown.
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Engine evaluation failed");
                    _store.ReportStatus($"Engine error: {ex.Message}");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(PollIntervalSeconds), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Shutting down.
                    break;
                }
            }

            // Log which streams were still active so the operator can see what was in flight
            // when the container stopped. Streams are left Active (stateless-by-design) so
            // they resume automatically on restart.
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var engine = scope.ServiceProvider.GetRequiredService<SpoolingEngine>();
                await engine.LogActiveStreamsOnShutdownAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not log active streams at shutdown");
            }
        }
    }
}
