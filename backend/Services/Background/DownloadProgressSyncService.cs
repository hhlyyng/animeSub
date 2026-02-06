using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace backend.Services.Background;

public class DownloadProgressSyncService : BackgroundService
{
    private readonly ILogger<DownloadProgressSyncService> _logger;
    private readonly IServiceProvider _serviceProvider;

    public DownloadProgressSyncService(
        ILogger<DownloadProgressSyncService> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Download Progress Sync Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncProgressFromQBittorrentAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in download progress sync loop");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }

        _logger.LogInformation("Download Progress Sync Service stopping");
    }

    private async Task SyncProgressFromQBittorrentAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Data.AnimeDbContext>();
        var qbittorrentService = scope.ServiceProvider.GetRequiredService<Services.Interfaces.IQBittorrentService>();

        var qbTorrents = await qbittorrentService.GetTorrentsAsync();

        if (qbTorrents.Count == 0)
        {
            _logger.LogDebug("No torrents to sync");
            return;
        }

        var syncedCount = 0;

        foreach (var qbTorrent in qbTorrents)
        {
            var existing = await dbContext.DownloadHistory
                .FirstOrDefaultAsync(d => d.TorrentHash == qbTorrent.Hash);

            if (existing != null)
            {
                existing.Progress = qbTorrent.Progress;
                existing.DownloadSpeed = qbTorrent.Dlspeed;
                existing.Eta = qbTorrent.Dlspeed > 0
                    ? (int?)((qbTorrent.Size * (1 - qbTorrent.Progress / 100)) / qbTorrent.Dlspeed)
                    : null;
                existing.NumSeeds = qbTorrent.NumSeeds;
                existing.NumLeechers = qbTorrent.NumLeechs;
                existing.SavePath = qbTorrent.SavePath;
                existing.Category = qbTorrent.Category;
                existing.LastSyncedAt = DateTime.UtcNow;

                existing.Status = qbTorrent.State switch
                {
                    "downloading" => Data.Entities.DownloadStatus.Downloading,
                    "pausedDL" => Data.Entities.DownloadStatus.Pending,
                    "stalledDL" => Data.Entities.DownloadStatus.Pending,
                    "uploading" => Data.Entities.DownloadStatus.Completed,
                    "stalledUP" => Data.Entities.DownloadStatus.Completed,
                    "queuedDL" => Data.Entities.DownloadStatus.Pending,
                    "completed" when qbTorrent.Progress >= 100 => Data.Entities.DownloadStatus.Completed,
                    _ => existing.Status
                };

                syncedCount++;
            }
        }

        if (syncedCount > 0)
        {
            await dbContext.SaveChangesAsync();
            _logger.LogInformation("Synced progress for {Count} torrents", syncedCount);
        }
    }
}
