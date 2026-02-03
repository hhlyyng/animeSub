using Microsoft.Extensions.Options;
using backend.Models.Configuration;
using backend.Services.Interfaces;

namespace backend.Services.Background;

/// <summary>
/// Background service for automatic RSS polling
/// Periodically checks all enabled subscriptions for new torrents
/// </summary>
public class RssPollingService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RssPollingService> _logger;
    private readonly MikanConfiguration _config;

    public RssPollingService(
        IServiceScopeFactory scopeFactory,
        ILogger<RssPollingService> logger,
        IOptions<MikanConfiguration> config)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _config = config.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RSS Polling Service starting");

        // Wait for startup delay to allow other services to initialize
        _logger.LogInformation("Waiting {Delay} seconds before first poll...", _config.StartupDelaySeconds);
        await Task.Delay(TimeSpan.FromSeconds(_config.StartupDelaySeconds), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_config.EnablePolling)
            {
                await PollSubscriptionsAsync(stoppingToken);
            }
            else
            {
                _logger.LogDebug("RSS polling is disabled in configuration");
            }

            // Wait for next poll interval
            var interval = TimeSpan.FromMinutes(_config.PollingIntervalMinutes);
            _logger.LogDebug("Next poll in {Minutes} minutes", _config.PollingIntervalMinutes);

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // Service is stopping
                break;
            }
        }

        _logger.LogInformation("RSS Polling Service stopping");
    }

    private async Task PollSubscriptionsAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting RSS poll cycle");

        try
        {
            // Create a new scope for scoped services
            using var scope = _scopeFactory.CreateScope();
            var subscriptionService = scope.ServiceProvider.GetRequiredService<ISubscriptionService>();

            var result = await subscriptionService.CheckAllSubscriptionsAsync();

            _logger.LogInformation(
                "Poll cycle completed: {Total} subscriptions, {Success} successful, {Failed} failed, {NewItems} new items",
                result.TotalSubscriptions,
                result.SuccessCount,
                result.FailedCount,
                result.TotalNewItems);

            // Log new downloads
            foreach (var checkResult in result.Results.Where(r => r.NewItemsCount > 0))
            {
                foreach (var title in checkResult.NewItemTitles)
                {
                    _logger.LogInformation("New download: {Title}", title);
                }
            }

            // Log failures
            foreach (var checkResult in result.Results.Where(r => !r.Success))
            {
                _logger.LogWarning(
                    "Failed to check subscription {Id}: {Error}",
                    checkResult.SubscriptionId,
                    checkResult.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during RSS poll cycle");
        }
    }

    public override async Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RSS Polling Service is stopping gracefully");
        await base.StopAsync(stoppingToken);
    }
}
