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

        // Filter by "anime" category to reduce unnecessary processing
        var qbTorrents = await qbittorrentService.GetTorrentsAsync("anime");

        if (qbTorrents.Count == 0)
        {
            _logger.LogDebug("No torrents to sync");
            return;
        }

        // Batch load all download history with hashes into a dictionary (case-insensitive)
        var allHistory = await dbContext.DownloadHistory
            .Where(d => d.TorrentHash != null && d.TorrentHash != "")
            .ToListAsync();

        var historyByHash = new Dictionary<string, Data.Entities.DownloadHistoryEntity>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in allHistory)
        {
            var key = h.TorrentHash!.Trim().ToUpperInvariant();
            historyByHash.TryAdd(key, h);
        }

        var syncedCount = 0;

        foreach (var qbTorrent in qbTorrents)
        {
            if (string.IsNullOrEmpty(qbTorrent.Hash))
                continue;

            var normalizedHash = qbTorrent.Hash.Trim().ToUpperInvariant();

            if (historyByHash.TryGetValue(normalizedHash, out var existing))
            {
                var progressPercent = ToProgressPercent(qbTorrent.Progress);

                existing.Progress = progressPercent;
                existing.DownloadSpeed = qbTorrent.Dlspeed;
                existing.Eta = CalculateEtaSeconds(qbTorrent.Size, progressPercent, qbTorrent.Dlspeed);
                existing.NumSeeds = qbTorrent.NumSeeds;
                existing.NumLeechers = qbTorrent.NumLeechs;
                existing.SavePath = qbTorrent.SavePath;
                existing.Category = qbTorrent.Category;
                existing.LastSyncedAt = DateTime.UtcNow;

                existing.Status = MapStateToStatus(qbTorrent.State, progressPercent, existing.Status);

                syncedCount++;
            }
        }

        if (syncedCount > 0)
        {
            await dbContext.SaveChangesAsync();
            _logger.LogInformation("Synced progress for {Count} torrents", syncedCount);
        }
    }

    public static double ToProgressPercent(double rawProgress)
    {
        if (double.IsNaN(rawProgress) || double.IsInfinity(rawProgress))
        {
            return 0;
        }

        // qBittorrent API returns 0-1 in most versions; keep compatibility with 0-100 payloads.
        var percent = rawProgress <= 1 ? rawProgress * 100 : rawProgress;
        return Math.Round(Math.Clamp(percent, 0, 100), 2);
    }

    public static int? CalculateEtaSeconds(long sizeBytes, double progressPercent, long downloadSpeedBytesPerSecond)
    {
        if (sizeBytes <= 0 || downloadSpeedBytesPerSecond <= 0)
        {
            return null;
        }

        var remainingRatio = Math.Clamp(1 - (progressPercent / 100.0), 0, 1);
        var remainingBytes = sizeBytes * remainingRatio;
        return (int)Math.Max(0, Math.Ceiling(remainingBytes / downloadSpeedBytesPerSecond));
    }

    public static Data.Entities.DownloadStatus MapStateToStatus(
        string? qbState,
        double progressPercent,
        Data.Entities.DownloadStatus currentStatus)
    {
        return qbState switch
        {
            "downloading" or "metaDL" or "forcedDL" or "allocating" or "checkingDL"
                => Data.Entities.DownloadStatus.Downloading,

            "pausedDL" or "stalledDL" or "queuedDL"
                => progressPercent >= 99.9 ? Data.Entities.DownloadStatus.Completed : Data.Entities.DownloadStatus.Pending,

            "uploading" or "stalledUP" or "queuedUP" or "forcedUP" or "checkingUP" or "completed"
                => Data.Entities.DownloadStatus.Completed,

            "error" or "missingFiles"
                => Data.Entities.DownloadStatus.Failed,

            _ => currentStatus
        };
    }
}
