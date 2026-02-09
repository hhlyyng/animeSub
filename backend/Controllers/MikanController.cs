using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using backend.Data;
using backend.Data.Entities;
using backend.Models.Dtos;
using backend.Services.Interfaces;
using backend.Services.Exceptions;
using backend.Services.Utils;

namespace backend.Controllers;

/// <summary>
/// API controller for Mikan anime download features
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class MikanController : ControllerBase
{
    private readonly IMikanClient _mikanClient;
    private readonly IBangumiClient _bangumiClient;
    private readonly IQBittorrentService _qbittorrentService;
    private readonly ILogger<MikanController> _logger;
    private readonly IServiceProvider _serviceProvider;
    private const string ManualSubscriptionTitle = "__manual_download_tracking__";
    private const string ManualSubscriptionMikanId = "manual";
    private const int ManualSubscriptionBangumiId = -1;

    public MikanController(
        IMikanClient mikanClient,
        IBangumiClient bangumiClient,
        IQBittorrentService qbittorrentService,
        ILogger<MikanController> logger,
        IServiceProvider serviceProvider)
    {
        _mikanClient = mikanClient;
        _bangumiClient = bangumiClient;
        _qbittorrentService = qbittorrentService;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Search for an anime on Mikan and return all seasons
    /// </summary>
    /// <param name="title">Anime title to search for</param>
    /// <param name="bangumiId">Optional Bangumi ID for caching the matched Mikan ID</param>
    /// <param name="season">Optional season number constraint</param>
    /// <returns>Search result with seasons</returns>
    [HttpGet("search")]
    public async Task<ActionResult<MikanSearchResult>> Search(
        [FromQuery] string title,
        [FromQuery] string? bangumiId = null,
        [FromQuery] int? season = null)
    {
        _logger.LogInformation("=== Mikan Search API Called ===");
        _logger.LogInformation("Path: {Path}", Request.Path);
        _logger.LogInformation("QueryString: {QueryString}", Request.QueryString);
        _logger.LogInformation("Title parameter: {Title}", title);
        _logger.LogInformation("BangumiId parameter: {BangumiId}", bangumiId);
        _logger.LogInformation("Season parameter: {Season}", season);
        _logger.LogInformation("Title length: {Length}", title?.Length ?? 0);

        if (string.IsNullOrWhiteSpace(title))
        {
            _logger.LogWarning("Search request with empty title");
            return BadRequest(new { message = "Title is required" });
        }

        _logger.LogInformation("Starting search for: {Title}", title);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await _mikanClient.SearchAnimeAsync(title);
            stopwatch.Stop();

            if (result == null)
            {
                _logger.LogWarning("No results found for: {Title} (elapsed: {Elapsed}ms)", title, stopwatch.ElapsedMilliseconds);
                return NotFound(new { message = $"No results found for: {title}" });
            }

            result = ApplySeasonConstraint(result, season);

            if (int.TryParse(bangumiId, out var parsedBangumiId) && parsedBangumiId > 0)
            {
                try
                {
                    await TryCacheMikanBangumiIdAsync(parsedBangumiId, result);
                }
                catch (Exception cacheEx)
                {
                    _logger.LogWarning(cacheEx, "Failed to cache MikanBangumiId for BangumiId={BangumiId}", parsedBangumiId);
                }
            }

            _logger.LogInformation(
                "Search SUCCESS for: {Title} (elapsed: {Elapsed}ms), Seasons: {Count}",
                title,
                stopwatch.ElapsedMilliseconds,
                result.Seasons.Count);

            return Ok(result);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Search failed for: {Title} (elapsed: {Elapsed}ms)", title, stopwatch.ElapsedMilliseconds);
            return StatusCode(500, new { message = $"Search failed: {ex.Message}" });
        }
    }

    /// <summary>
    /// Get parsed RSS feed for a specific anime
    /// </summary>
    /// <param name="mikanId">Mikan Bangumi ID</param>
    /// <param name="bangumiId">Optional Bangumi ID for episode normalization</param>
    /// <returns>Feed with parsed items and available filters</returns>
    [HttpGet("feed")]
    public async Task<ActionResult<MikanFeedResponse>> GetFeed([FromQuery] string mikanId, [FromQuery] string? bangumiId = null)
    {
        if (string.IsNullOrWhiteSpace(mikanId))
        {
            return BadRequest(new { message = "Mikan Bangumi ID is required" });
        }

        try
        {
            var feed = await _mikanClient.GetParsedFeedAsync(mikanId);
            var expectedEpisodes = await TryGetExpectedEpisodeCountAsync(bangumiId);
            ApplyEpisodeOffsetNormalization(feed, expectedEpisodes);
            UpdateLatestMetadata(feed);
            return Ok(feed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching feed for Mikan ID: {MikanId}", mikanId);
            return StatusCode(500, new { message = "Failed to fetch feed" });
        }
    }

    /// <summary>
    /// Filter RSS items based on user preferences
    /// </summary>
    /// <param name="mikanId">Mikan Bangumi ID</param>
    /// <param name="resolution">Resolution filter (optional)</param>
    /// <param name="subgroup">Subgroup filter (optional)</param>
    /// <param name="subtitleType">Subtitle type filter (optional)</param>
    /// <returns>Filtered RSS items</returns>
    [HttpGet("filter")]
    public async Task<ActionResult<List<ParsedRssItem>>> Filter(
        [FromQuery] string mikanId,
        [FromQuery] string? resolution = null,
        [FromQuery] string? subgroup = null,
        [FromQuery] string? subtitleType = null)
    {
        if (string.IsNullOrWhiteSpace(mikanId))
        {
            return BadRequest(new { message = "Mikan Bangumi ID is required" });
        }

        try
        {
            var feed = await _mikanClient.GetParsedFeedAsync(mikanId);
            var filtered = ApplyFilters(feed.Items, resolution, subgroup, subtitleType);
            return Ok(filtered);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error filtering feed for Mikan ID: {MikanId}", mikanId);
            return StatusCode(500, new { message = "Failed to filter feed" });
        }
    }

    /// <summary>
    /// Download a torrent to qBittorrent
    /// </summary>
    /// <param name="request">Download request</param>
    /// <returns>Success or failure</returns>
    [HttpPost("download")]
    public async Task<ActionResult> Download([FromBody] DownloadTorrentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MagnetLink) && string.IsNullOrWhiteSpace(request.TorrentUrl))
        {
            return BadRequest(new { message = "Magnet link or torrent URL is required" });
        }

        var normalizedHash = TorrentHashHelper.ResolveHash(
            request.TorrentHash,
            request.MagnetLink,
            request.TorrentUrl);

        if (string.IsNullOrWhiteSpace(normalizedHash))
        {
            return BadRequest(new { message = "Torrent hash is required" });
        }

        var torrentInput = !string.IsNullOrWhiteSpace(request.MagnetLink)
            ? request.MagnetLink
            : request.TorrentUrl!;

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimeDbContext>();

        var existingDownload = await db.DownloadHistory
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.TorrentHash == normalizedHash);

        if (existingDownload != null)
        {
            return Ok(new
            {
                message = "Download already exists",
                hash = normalizedHash,
                alreadyExists = true
            });
        }

        bool success;
        try
        {
            success = await _qbittorrentService.AddTorrentWithTrackingAsync(
                torrentInput,
                normalizedHash,
                request.Title,
                0,
                DownloadSource.Manual);
        }
        catch (QBittorrentUnavailableException ex)
        {
            _logger.LogWarning(ex, "qBittorrent unavailable while pushing manual download {Hash}", normalizedHash);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = ex.Message,
                reason = ex.Reason,
                retryAfterUtc = ex.RetryAfterUtc
            });
        }

        if (!success)
        {
            return StatusCode(500, new { message = "Failed to add torrent to qBittorrent" });
        }

        var manualSubscriptionId = await EnsureManualSubscriptionAsync(db);

        var download = new DownloadHistoryEntity
        {
            SubscriptionId = manualSubscriptionId,
            TorrentUrl = torrentInput,
            TorrentHash = normalizedHash,
            Title = request.Title,
            Status = DownloadStatus.Pending,
            Source = DownloadSource.Manual,
            PublishedAt = DateTime.UtcNow,
            DiscoveredAt = DateTime.UtcNow,
            DownloadedAt = DateTime.UtcNow,
            Progress = 0
        };

        db.DownloadHistory.Add(download);
        await db.SaveChangesAsync();

        _logger.LogInformation("Manual download added: Hash={Hash}, Source={Source}", normalizedHash, DownloadSource.Manual);
        return Ok(new { message = "Download task queued successfully", hash = normalizedHash });
    }

    /// <summary>
    /// Check download status of torrents
    /// </summary>
    /// <returns>List of torrents in database</returns>
    [HttpGet("torrents")]
    public async Task<ActionResult> GetTorrents()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AnimeDbContext>();
            var qbTorrents = await _qbittorrentService.GetTorrentsAsync();
            var qbByHash = qbTorrents
                .Where(t => !string.IsNullOrWhiteSpace(t.Hash))
                .GroupBy(t => NormalizeHash(t.Hash))
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var torrents = await db.DownloadHistory
                .Where(d => d.Source == DownloadSource.Manual)
                .OrderByDescending(d => d.DownloadedAt ?? d.DiscoveredAt)
                .ToListAsync();

            var response = torrents.Select(d =>
            {
                var normalizedHash = NormalizeHash(d.TorrentHash);
                var hasRealtime = qbByHash.TryGetValue(normalizedHash, out var qbTorrent);

                var state = hasRealtime ? qbTorrent!.State : ToFallbackTorrentState(d.Status);
                var progress = hasRealtime ? ClampProgressPercent(qbTorrent!.Progress) : ClampProgressPercent(d.Progress);
                var size = hasRealtime && qbTorrent!.Size > 0 ? qbTorrent.Size : d.FileSize ?? 0;
                var eta = hasRealtime ? qbTorrent!.Eta : d.Eta;
                var downloadSpeed = hasRealtime ? qbTorrent!.Dlspeed : d.DownloadSpeed ?? 0;
                var numSeeds = hasRealtime ? qbTorrent!.NumSeeds : d.NumSeeds ?? 0;
                var numLeechers = hasRealtime ? qbTorrent!.NumLeechs : d.NumLeechers ?? 0;

                return new
                {
                    hash = normalizedHash,
                    name = hasRealtime ? qbTorrent!.Name : d.Title,
                    size,
                    state,
                    progress,
                    downloadSpeed,
                    eta,
                    numSeeds,
                    numLeechers
                };
            }).ToList();

            return Ok(response);
        }
        catch (QBittorrentUnavailableException ex)
        {
            _logger.LogWarning(ex, "qBittorrent unavailable while fetching torrents");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = ex.Message,
                reason = ex.Reason,
                retryAfterUtc = ex.RetryAfterUtc
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting torrents list");
            return StatusCode(500, new { message = "Failed to get torrents" });
        }
    }

    [HttpPost("torrents/{hash}/pause")]
    public async Task<ActionResult> PauseTorrent([FromRoute] string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return BadRequest(new { message = "Torrent hash is required" });
        }

        try
        {
            await _qbittorrentService.PauseTorrentAsync(hash);

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AnimeDbContext>();
            var existing = await db.DownloadHistory
                .FirstOrDefaultAsync(d => d.TorrentHash == hash && d.Source == DownloadSource.Manual);

            if (existing != null)
            {
                existing.Status = DownloadStatus.Pending;
                existing.LastSyncedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }

            return Ok(new { message = "Torrent paused", hash });
        }
        catch (QBittorrentUnavailableException ex)
        {
            _logger.LogWarning(ex, "qBittorrent unavailable while pausing torrent {Hash}", hash);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = ex.Message,
                reason = ex.Reason,
                retryAfterUtc = ex.RetryAfterUtc
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pause torrent {Hash}", hash);
            return StatusCode(500, new { message = "Failed to pause torrent" });
        }
    }

    [HttpPost("torrents/{hash}/resume")]
    public async Task<ActionResult> ResumeTorrent([FromRoute] string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return BadRequest(new { message = "Torrent hash is required" });
        }

        try
        {
            await _qbittorrentService.ResumeTorrentAsync(hash);

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AnimeDbContext>();
            var existing = await db.DownloadHistory
                .FirstOrDefaultAsync(d => d.TorrentHash == hash && d.Source == DownloadSource.Manual);

            if (existing != null)
            {
                existing.Status = DownloadStatus.Downloading;
                existing.LastSyncedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }

            return Ok(new { message = "Torrent resumed", hash });
        }
        catch (QBittorrentUnavailableException ex)
        {
            _logger.LogWarning(ex, "qBittorrent unavailable while resuming torrent {Hash}", hash);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = ex.Message,
                reason = ex.Reason,
                retryAfterUtc = ex.RetryAfterUtc
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resume torrent {Hash}", hash);
            return StatusCode(500, new { message = "Failed to resume torrent" });
        }
    }

    [HttpDelete("torrents/{hash}")]
    public async Task<ActionResult> RemoveTorrent([FromRoute] string hash, [FromQuery] bool deleteFiles = false)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return BadRequest(new { message = "Torrent hash is required" });
        }

        try
        {
            var removed = await _qbittorrentService.DeleteTorrentAsync(hash, deleteFiles);
            if (!removed)
            {
                return StatusCode(500, new { message = "Failed to remove torrent from qBittorrent" });
            }

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AnimeDbContext>();
            var records = await db.DownloadHistory
                .Where(d => d.TorrentHash == hash && d.Source == DownloadSource.Manual)
                .ToListAsync();

            if (records.Count > 0)
            {
                db.DownloadHistory.RemoveRange(records);
                await db.SaveChangesAsync();
            }

            return Ok(new { message = "Torrent removed", hash });
        }
        catch (QBittorrentUnavailableException ex)
        {
            _logger.LogWarning(ex, "qBittorrent unavailable while removing torrent {Hash}", hash);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = ex.Message,
                reason = ex.Reason,
                retryAfterUtc = ex.RetryAfterUtc
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove torrent {Hash}", hash);
            return StatusCode(500, new { message = "Failed to remove torrent" });
        }
    }

    private static List<ParsedRssItem> ApplyFilters(
        List<ParsedRssItem> items,
        string? resolution,
        string? subgroup,
        string? subtitleType)
    {
        var filtered = items.AsEnumerable();

        if (!string.IsNullOrEmpty(resolution) && !resolution.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(item =>
                item.Resolution != null && item.Resolution.Equals(resolution, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(subgroup) && !subgroup.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(item =>
                item.Subgroup != null && item.Subgroup.Equals(subgroup, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(subtitleType) && !subtitleType.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(item =>
                item.SubtitleType != null && item.SubtitleType.Equals(subtitleType, StringComparison.OrdinalIgnoreCase));
        }

        return filtered.OrderByDescending(i => i.PublishedAt).ToList();
    }

    private static MikanSearchResult ApplySeasonConstraint(MikanSearchResult searchResult, int? season)
    {
        if (!season.HasValue || season.Value <= 0 || searchResult.Seasons.Count <= 1)
        {
            return searchResult;
        }

        var candidates = searchResult.Seasons
            .Where(s => MatchSeasonNumber(s, season.Value))
            .OrderByDescending(s => s.Year)
            .ToList();

        if (candidates.Count == 0)
        {
            return searchResult;
        }

        searchResult.Seasons = new List<MikanSeasonInfo> { candidates[0] };
        searchResult.DefaultSeason = 0;
        return searchResult;
    }

    private static bool MatchSeasonNumber(MikanSeasonInfo season, int expectedSeason)
    {
        if (season.SeasonNumber.HasValue)
        {
            return season.SeasonNumber.Value == expectedSeason;
        }

        var extracted = TryExtractSeasonNumber(season.SeasonName);
        return extracted.HasValue && extracted.Value == expectedSeason;
    }

    private static int? TryExtractSeasonNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = Regex.Match(
            text,
            @"(?:season\s*(\d+)|\bs\s*0?(\d+)\b|(\d+)(?:st|nd|rd|th)\s*season|\u7b2c\s*([0-9\u4e00\u4e8c\u4e09\u56db\u4e94\u516d\u4e03\u516b\u4e5d\u5341]+)\s*\u5b63)",
            RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            return null;
        }

        for (var i = 1; i < match.Groups.Count; i++)
        {
            var value = match.Groups[i].Value;
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (int.TryParse(value, out var parsed) && parsed > 0)
            {
                return parsed;
            }

            var chinese = value switch
            {
                "\u4e00" => 1,
                "\u4e8c" => 2,
                "\u4e09" => 3,
                "\u56db" => 4,
                "\u4e94" => 5,
                "\u516d" => 6,
                "\u4e03" => 7,
                "\u516b" => 8,
                "\u4e5d" => 9,
                "\u5341" => 10,
                _ => 0
            };

            if (chinese > 0)
            {
                return chinese;
            }
        }

        return null;
    }

    private async Task<int?> TryGetExpectedEpisodeCountAsync(string? bangumiId)
    {
        if (!int.TryParse(bangumiId, out var parsedBangumiId) || parsedBangumiId <= 0)
        {
            return null;
        }

        try
        {
            var detail = await _bangumiClient.GetSubjectDetailAsync(parsedBangumiId);
            if (!detail.TryGetProperty("eps", out var epsElement))
            {
                return null;
            }

            if (epsElement.ValueKind == JsonValueKind.Number &&
                epsElement.TryGetInt32(out var numeric) &&
                numeric > 0)
            {
                return numeric;
            }

            if (epsElement.ValueKind == JsonValueKind.String &&
                int.TryParse(epsElement.GetString(), out var parsed) &&
                parsed > 0)
            {
                return parsed;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Bangumi episode metadata for BangumiId {BangumiId}", bangumiId);
        }

        return null;
    }

    private static void ApplyEpisodeOffsetNormalization(MikanFeedResponse feed, int? expectedEpisodes)
    {
        var episodeNumbers = feed.Items
            .Where(item => item.Episode.HasValue)
            .Select(item => item.Episode!.Value)
            .OrderBy(value => value)
            .ToList();

        if (episodeNumbers.Count == 0)
        {
            return;
        }

        var minEpisode = episodeNumbers.First();
        var maxEpisode = episodeNumbers.Last();
        if (minEpisode <= 1)
        {
            return;
        }

        var span = maxEpisode - minEpisode + 1;
        var seasonNumber = TryExtractSeasonNumber(feed.SeasonName) ?? 1;
        var offset = 0;

        if (expectedEpisodes.HasValue && expectedEpisodes.Value > 0)
        {
            var expected = expectedEpisodes.Value;
            if (maxEpisode > expected && span <= expected + 2)
            {
                offset = minEpisode - 1;
            }
        }
        else if (seasonNumber > 1 && span <= 30)
        {
            // Fallback heuristic when Bangumi metadata is unavailable.
            offset = minEpisode - 1;
        }

        if (offset <= 0)
        {
            return;
        }

        foreach (var item in feed.Items)
        {
            if (!item.Episode.HasValue)
            {
                continue;
            }

            var normalized = item.Episode.Value - offset;
            if (normalized > 0)
            {
                item.Episode = normalized;
            }
        }

        feed.EpisodeOffset = offset;
    }

    private static void UpdateLatestMetadata(MikanFeedResponse feed)
    {
        var latestItem = feed.Items.OrderByDescending(item => item.PublishedAt).FirstOrDefault();
        if (latestItem != null)
        {
            feed.LatestPublishedAt = latestItem.PublishedAt;
            feed.LatestTitle = latestItem.Title;
        }

        var latestEpisode = feed.Items
            .Where(item => item.Episode.HasValue)
            .Select(item => item.Episode!.Value)
            .DefaultIfEmpty(0)
            .Max();

        feed.LatestEpisode = latestEpisode > 0 ? latestEpisode : null;
    }

    private async Task<int> EnsureManualSubscriptionAsync(AnimeDbContext db)
    {
        var existing = await db.Subscriptions.FirstOrDefaultAsync(s =>
            s.BangumiId == ManualSubscriptionBangumiId || s.Title == ManualSubscriptionTitle);

        if (existing != null)
        {
            return existing.Id;
        }

        var now = DateTime.UtcNow;
        var manualSubscription = new SubscriptionEntity
        {
            BangumiId = ManualSubscriptionBangumiId,
            Title = ManualSubscriptionTitle,
            MikanBangumiId = ManualSubscriptionMikanId,
            IsEnabled = false,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Subscriptions.Add(manualSubscription);
        await db.SaveChangesAsync();
        return manualSubscription.Id;
    }

    private async Task TryCacheMikanBangumiIdAsync(int bangumiId, MikanSearchResult searchResult)
    {
        var defaultSeason = searchResult.Seasons.ElementAtOrDefault(searchResult.DefaultSeason)
            ?? searchResult.Seasons.FirstOrDefault();

        if (defaultSeason == null || string.IsNullOrWhiteSpace(defaultSeason.MikanBangumiId))
        {
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimeDbContext>();
        var anime = await db.AnimeInfos.FirstOrDefaultAsync(a => a.BangumiId == bangumiId);
        if (anime == null)
        {
            return;
        }

        if (anime.MikanBangumiId == defaultSeason.MikanBangumiId)
        {
            return;
        }

        anime.MikanBangumiId = defaultSeason.MikanBangumiId;
        anime.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private static string NormalizeHash(string? hash)
    {
        return hash?.Trim().ToUpperInvariant() ?? string.Empty;
    }

    private static string ToFallbackTorrentState(DownloadStatus status)
    {
        return status switch
        {
            DownloadStatus.Downloading => "downloading",
            DownloadStatus.Completed => "completed",
            DownloadStatus.Failed => "error",
            _ => "pausedDL"
        };
    }

    private static double ClampProgressPercent(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return Math.Round(Math.Clamp(value, 0, 100), 2);
    }
}
