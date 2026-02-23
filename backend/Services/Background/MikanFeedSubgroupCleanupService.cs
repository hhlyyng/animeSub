using System.Text.RegularExpressions;
using backend.Data;
using backend.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace backend.Services.Background;

/// <summary>
/// One-shot startup task that cleans subgroup noise from persisted Mikan feed cache.
/// </summary>
public class MikanFeedSubgroupCleanupService : IHostedService
{
    private const int MinSubgroupAirYear = 2025;
    private const string CleanupSentinelFileName = ".mikan-subgroup-cleanup-v1.done";

    private static readonly Regex NonSubgroupTokenRegex = new(
        @"^(?:\d{1,4}|(?:2160|1080|720)p|4k|x26[45]|hevc|aac|flac|av1|mkv|mp4|chs|cht|gb|big5)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SubgroupNoiseTokenRegex = new(
        @"\b(?:2160p|1080p|720p|4k|x26[45]|hevc|av1|aac|flac|mp4|mkv|chs|cht)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SubgroupEpisodeHintRegex = new(
        @"(?:\bS\d{1,2}\b|\bE\d{1,3}\b|\bEP?\s*\d{1,3}\b|\u7b2c\s*\d+\s*[\u8bdd\u8a71\u96c6]|Season\s*\d+|After\s*Story|\u5267\u573a\u7248|\u5408\u96c6|\u5b8c\u7ed3)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ILogger<MikanFeedSubgroupCleanupService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly string _sentinelFilePath;

    public MikanFeedSubgroupCleanupService(
        ILogger<MikanFeedSubgroupCleanupService> logger,
        IServiceProvider serviceProvider,
        IWebHostEnvironment environment)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _sentinelFilePath = Path.Combine(environment.ContentRootPath, "Data", CleanupSentinelFileName);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(_sentinelFilePath))
        {
            _logger.LogInformation("Mikan subgroup cleanup skipped (already completed once). Marker={MarkerPath}", _sentinelFilePath);
            return;
        }

        _logger.LogInformation("Mikan subgroup cleanup task started");
        var completed = false;

        try
        {
            await RunCleanupAsync(cancellationToken);
            completed = true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Mikan subgroup cleanup task cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mikan subgroup cleanup task failed");
        }
        finally
        {
            if (completed)
            {
                try
                {
                    var directory = Path.GetDirectoryName(_sentinelFilePath);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    await File.WriteAllTextAsync(
                        _sentinelFilePath,
                        $"completed_at_utc={DateTime.UtcNow:O}",
                        cancellationToken);

                    _logger.LogInformation(
                        "Mikan subgroup cleanup marked as completed. Marker={MarkerPath}",
                        _sentinelFilePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Mikan subgroup cleanup finished but failed to write marker. It may run again on next startup.");
                }
            }

            _logger.LogInformation("Mikan subgroup cleanup task completed");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private async Task RunCleanupAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AnimeDbContext>();
        var titleParser = scope.ServiceProvider.GetRequiredService<ITorrentTitleParser>();

        var eligibleMikanIds = await dbContext.AnimeInfos
            .AsNoTracking()
            .Where(anime => anime.MikanBangumiId != null && anime.AirDate != null)
            .Select(anime => new
            {
                MikanBangumiId = anime.MikanBangumiId!,
                AirDate = anime.AirDate!
            })
            .ToListAsync(cancellationToken);

        var eligibleMikanIdSet = eligibleMikanIds
            .Where(item => !string.IsNullOrWhiteSpace(item.MikanBangumiId) &&
                           TryParseAirYear(item.AirDate, out var year) &&
                           year >= MinSubgroupAirYear)
            .Select(item => item.MikanBangumiId.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var items = await dbContext.MikanFeedItems
            .ToListAsync(cancellationToken);

        var scanned = 0;
        var updated = 0;
        var cleared = 0;
        var preserved = 0;

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            scanned++;

            var parsed = titleParser.ParseTitle(item.Title);
            var normalizedFromTitle = NormalizeSubgroup(parsed.Subgroup);

            var shouldKeepSubgroup = eligibleMikanIdSet.Contains(item.MikanBangumiId.Trim());
            var rewrittenSubgroup =
                shouldKeepSubgroup && !string.IsNullOrWhiteSpace(normalizedFromTitle) && IsValidSubgroupOption(normalizedFromTitle)
                    ? normalizedFromTitle
                    : null;

            if (!string.Equals(item.Subgroup?.Trim(), rewrittenSubgroup, StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(item.Subgroup) && string.IsNullOrWhiteSpace(rewrittenSubgroup))
                {
                    cleared++;
                }

                item.Subgroup = rewrittenSubgroup;
                updated++;
            }

            if (!string.IsNullOrWhiteSpace(rewrittenSubgroup))
            {
                preserved++;
            }
        }

        if (updated > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "Mikan subgroup cleanup finished. Scanned={Scanned}, Updated={Updated}, Cleared={Cleared}, Preserved={Preserved}, EligibleMikanIds={EligibleMikanIds}",
            scanned,
            updated,
            cleared,
            preserved,
            eligibleMikanIdSet.Count);
    }

    private static bool TryParseAirYear(string airDate, out int year)
    {
        year = 0;
        if (string.IsNullOrWhiteSpace(airDate) || airDate.Length < 4)
        {
            return false;
        }

        return int.TryParse(airDate[..4], out year);
    }

    private static string? NormalizeSubgroup(string? subgroup)
    {
        if (string.IsNullOrWhiteSpace(subgroup))
        {
            return null;
        }

        var trimmed = subgroup.Trim();
        if (trimmed.Length < 2 || trimmed.Length > 48)
        {
            return null;
        }

        return NonSubgroupTokenRegex.IsMatch(trimmed) ? null : trimmed;
    }

    private static bool IsValidSubgroupOption(string value)
    {
        if (SubgroupNoiseTokenRegex.IsMatch(value) || SubgroupEpisodeHintRegex.IsMatch(value))
        {
            return false;
        }

        var whitespaceCount = value.Count(char.IsWhiteSpace);
        if (whitespaceCount > 3)
        {
            return false;
        }

        var allNumericPunctuation = value.All(ch => char.IsDigit(ch) || ch is '.' or '_' or '-');
        return !allNumericPunctuation;
    }
}
