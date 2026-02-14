using backend.Data;
using backend.Services.Interfaces;
using backend.Services.Utilities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace backend.Services.Background;

/// <summary>
/// One-shot startup task that backfills title fields on legacy persisted records.
/// </summary>
public class AnimeTitleBackfillService : BackgroundService
{
    private readonly ILogger<AnimeTitleBackfillService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private const int SaveBatchSize = 100;
    private static readonly TimeSpan RequestDelay = TimeSpan.FromMilliseconds(80);

    public AnimeTitleBackfillService(
        ILogger<AnimeTitleBackfillService> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Anime title backfill service started");

        try
        {
            await RunBackfillAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Anime title backfill service cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Anime title backfill service failed");
        }
        finally
        {
            _logger.LogInformation("Anime title backfill service completed");
        }
    }

    private async Task RunBackfillAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AnimeDbContext>();
        var bangumiClient = scope.ServiceProvider.GetRequiredService<IBangumiClient>();

        var candidates = await dbContext.AnimeInfos
            .Where(a =>
                a.NameJapanese == null || a.NameJapanese == "" ||
                a.NameChinese == null || a.NameChinese == "" ||
                a.NameEnglish == null || a.NameEnglish == "")
            .OrderBy(a => a.BangumiId)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            _logger.LogInformation("No anime title backfill candidates found");
            return;
        }

        _logger.LogInformation("Anime title backfill candidates: {Count}", candidates.Count);

        var scanned = 0;
        var updated = 0;
        var failed = 0;
        var dirtyCount = 0;

        foreach (var anime in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            scanned++;

            string sourceName = "";
            string sourceNameCn = "";

            try
            {
                var subjectDetail = await bangumiClient.GetSubjectDetailAsync(anime.BangumiId);
                sourceName = TryReadString(subjectDetail, "name");
                sourceNameCn = TryReadString(subjectDetail, "name_cn");
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogDebug(ex, "Failed to fetch Bangumi subject detail for title backfill. BangumiId={BangumiId}", anime.BangumiId);
            }

            var detectName = FirstNonEmpty(sourceName, sourceNameCn, anime.NameJapanese, anime.NameChinese, anime.NameEnglish);
            var seedChTitle = FirstNonEmpty(anime.NameChinese, sourceNameCn);

            var resolved = TitleLanguageResolver.ResolveFromName(
                detectName,
                jpTitle: anime.NameJapanese,
                chTitle: seedChTitle,
                enTitle: anime.NameEnglish);

            var newJpTitle = NullIfWhiteSpace(resolved.jpTitle);
            var newChTitle = NullIfWhiteSpace(resolved.chTitle);
            var newEnTitle = NullIfWhiteSpace(resolved.enTitle);

            if (!Same(anime.NameJapanese, newJpTitle) ||
                !Same(anime.NameChinese, newChTitle) ||
                !Same(anime.NameEnglish, newEnTitle))
            {
                anime.NameJapanese = newJpTitle;
                anime.NameChinese = newChTitle;
                anime.NameEnglish = newEnTitle;
                anime.UpdatedAt = DateTime.UtcNow;
                updated++;
                dirtyCount++;
            }

            if (dirtyCount >= SaveBatchSize)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                dirtyCount = 0;
            }

            if (scanned < candidates.Count)
            {
                await Task.Delay(RequestDelay, cancellationToken);
            }
        }

        if (dirtyCount > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (updated > 0)
        {
            var topCaches = await dbContext.TopAnimeCaches.ToListAsync(cancellationToken);
            if (topCaches.Count > 0)
            {
                dbContext.TopAnimeCaches.RemoveRange(topCaches);
                await dbContext.SaveChangesAsync(cancellationToken);
                _logger.LogInformation(
                    "Cleared top list cache after title backfill. Removed={Removed}",
                    topCaches.Count);
            }
        }

        _logger.LogInformation(
            "Anime title backfill finished. Scanned={Scanned}, Updated={Updated}, DetailFetchFailed={Failed}",
            scanned,
            updated,
            failed);
    }

    private static string TryReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) ? value.GetString() ?? "" : "";
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool Same(string? left, string? right)
    {
        return string.Equals(
            left?.Trim() ?? string.Empty,
            right?.Trim() ?? string.Empty,
            StringComparison.Ordinal);
    }
}
