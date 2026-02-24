using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Authorization;
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
[Authorize]
public class MikanController : ControllerBase
{
    private readonly IMikanClient _mikanClient;
    private readonly IBangumiClient _bangumiClient;
    private readonly IAniListClient _aniListClient;
    private readonly IJikanClient _jikanClient;
    private readonly IQBittorrentService _qbittorrentService;
    private readonly ILogger<MikanController> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private const string ManualSubscriptionTitle = "__manual_download_tracking__";
    private const string ManualSubscriptionMikanId = "manual";
    private const int ManualSubscriptionBangumiId = -1;

    public MikanController(
        IMikanClient mikanClient,
        IBangumiClient bangumiClient,
        IAniListClient aniListClient,
        IJikanClient jikanClient,
        IQBittorrentService qbittorrentService,
        ILogger<MikanController> logger,
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory)
    {
        _mikanClient = mikanClient;
        _bangumiClient = bangumiClient;
        _aniListClient = aniListClient;
        _jikanClient = jikanClient;
        _qbittorrentService = qbittorrentService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _httpClientFactory = httpClientFactory;
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
    /// Search for anime entries on Mikan (returns all matching entries with images)
    /// </summary>
    /// <param name="title">Anime title to search for</param>
    /// <returns>List of matching anime entries</returns>
    [HttpGet("search-entries")]
    public async Task<ActionResult<List<MikanAnimeEntry>>> SearchEntries([FromQuery] string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return BadRequest(new { message = "Title is required" });
        }

        try
        {
            var entries = await _mikanClient.SearchAnimeEntriesAsync(title);
            return Ok(entries);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SearchEntries failed for title: {Title}", title);
            return StatusCode(500, new { message = $"Search failed: {ex.Message}" });
        }
    }

    /// <summary>
    /// Correct the Mikan Bangumi ID for a given Bangumi ID
    /// </summary>
    /// <param name="request">Correction request with bangumiId and mikanBangumiId</param>
    /// <returns>Success or failure</returns>
    [HttpPost("correct-bangumi-id")]
    public async Task<ActionResult> CorrectBangumiId([FromBody] CorrectMikanBangumiIdRequest request)
    {
        if (request.BangumiId <= 0)
        {
            return BadRequest(new { message = "Valid BangumiId is required" });
        }

        if (string.IsNullOrWhiteSpace(request.MikanBangumiId))
        {
            return BadRequest(new { message = "MikanBangumiId is required" });
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AnimeDbContext>();
            var anime = await db.AnimeInfos.FirstOrDefaultAsync(a => a.BangumiId == request.BangumiId);

            if (anime == null)
            {
                return NotFound(new { message = $"Anime with BangumiId {request.BangumiId} not found" });
            }

            anime.MikanBangumiId = request.MikanBangumiId.Trim();
            anime.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            _logger.LogInformation(
                "Corrected MikanBangumiId for BangumiId={BangumiId} to {MikanBangumiId}",
                request.BangumiId,
                request.MikanBangumiId);

            return Ok(new { message = "Correction saved", bangumiId = request.BangumiId, mikanBangumiId = request.MikanBangumiId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to correct MikanBangumiId for BangumiId={BangumiId}", request.BangumiId);
            return StatusCode(500, new { message = "Failed to save correction" });
        }
    }

    /// <summary>
    /// Get subgroup name-to-ID mapping for a Mikan anime
    /// </summary>
    /// <param name="mikanBangumiId">Mikan Bangumi ID</param>
    /// <returns>List of subgroups with their numeric IDs</returns>
    [HttpGet("subgroups")]
    public async Task<ActionResult<List<MikanSubgroupInfo>>> GetSubgroups([FromQuery] string mikanBangumiId)
    {
        if (string.IsNullOrWhiteSpace(mikanBangumiId))
        {
            return BadRequest(new { message = "Mikan Bangumi ID is required" });
        }

        try
        {
            var subgroups = await _mikanClient.GetSubgroupsAsync(mikanBangumiId);
            return Ok(subgroups);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get subgroups for Mikan ID: {MikanBangumiId}", mikanBangumiId);
            return StatusCode(500, new { message = "Failed to get subgroups" });
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
            var parsedBangumiId = TryParsePositiveInt(bangumiId);
            var bangumiDetail = await TryGetBangumiSubjectDetailAsync(parsedBangumiId);
            var expectedEpisodes = TryGetExpectedEpisodeCount(bangumiDetail);
            var isMovieOrSingleEpisode = IsMovieOrSingleEpisodeWork(bangumiDetail, expectedEpisodes);

            if (isMovieOrSingleEpisode)
            {
                _logger.LogInformation(
                    "Detected movie/single-episode work for BangumiId={BangumiId}. Applying one-shot validation.",
                    parsedBangumiId);
                ApplyMovieOrSingleEpisodeValidation(feed);
                UpdateLatestMetadata(feed);
                return Ok(feed);
            }

            var normalized = false;
            if (parsedBangumiId.HasValue)
            {
                // Method 0: scrape bgm.tv prg_list for the season's starting episode offset
                var prgListInfo = await TryGetBangumiPrgListInfoAsync(parsedBangumiId.Value);
                if (prgListInfo.HasValue)
                {
                    normalized = ApplyExplicitOffsetNormalization(
                        feed,
                        prgListInfo.Value.offset,
                        prgListInfo.Value.totalEpisodes ?? expectedEpisodes);
                    if (normalized)
                    {
                        _logger.LogInformation(
                            "Normalized via prg_list: BangumiId={Id} offset={Offset}",
                            parsedBangumiId.Value, prgListInfo.Value.offset);
                    }
                }

                if (!normalized)
                {
                    // Method 1: Bangumi API episode map (sort vs ep)
                    normalized = await TryApplyBangumiEpisodeMapNormalizationAsync(
                        feed,
                        parsedBangumiId.Value,
                        expectedEpisodes);
                }

                if (!normalized)
                {
                    // Method 2: Bangumi prequel relation chain
                    var bangumiOffset = await TryResolveOffsetFromBangumiRelationsAsync(parsedBangumiId.Value);
                    if (bangumiOffset.HasValue)
                    {
                        normalized = ApplyExplicitOffsetNormalization(feed, bangumiOffset.Value, expectedEpisodes);
                    }
                }
            }

            if (!normalized)
            {
                var anilistSearchTitle = TryResolveAniListSearchTitle(bangumiDetail, feed);
                if (!string.IsNullOrWhiteSpace(anilistSearchTitle))
                {
                    var aniListOffset = await TryResolveOffsetFromAniListJikanAsync(anilistSearchTitle);
                    if (aniListOffset.HasValue)
                    {
                        normalized = ApplyExplicitOffsetNormalization(feed, aniListOffset.Value, expectedEpisodes);
                    }
                }
            }

            if (!normalized)
            {
                ApplyEpisodeOffsetNormalization(feed, expectedEpisodes);
            }

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
        int? animeBangumiId = request.BangumiId.HasValue && request.BangumiId.Value > 0
            ? request.BangumiId.Value
            : null;
        var animeMikanBangumiId = string.IsNullOrWhiteSpace(request.MikanBangumiId)
            ? null
            : request.MikanBangumiId.Trim();
        var animeTitle = string.IsNullOrWhiteSpace(request.AnimeTitle)
            ? request.Title
            : request.AnimeTitle.Trim();

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimeDbContext>();

        var existingDownload = await db.DownloadHistory
            .FirstOrDefaultAsync(d => d.TorrentHash == normalizedHash);

        if (existingDownload != null)
        {
            var shouldUpdateExisting = false;
            if (!existingDownload.AnimeBangumiId.HasValue && animeBangumiId.HasValue)
            {
                existingDownload.AnimeBangumiId = animeBangumiId.Value;
                shouldUpdateExisting = true;
            }

            if (string.IsNullOrWhiteSpace(existingDownload.AnimeMikanBangumiId) &&
                !string.IsNullOrWhiteSpace(animeMikanBangumiId))
            {
                existingDownload.AnimeMikanBangumiId = animeMikanBangumiId;
                shouldUpdateExisting = true;
            }

            if (string.IsNullOrWhiteSpace(existingDownload.AnimeTitle) &&
                !string.IsNullOrWhiteSpace(animeTitle))
            {
                existingDownload.AnimeTitle = animeTitle;
                shouldUpdateExisting = true;
            }

            // Re-associate with subscription if download was previously manual
            if (request.SubscriptionId.HasValue && request.SubscriptionId.Value > 0 &&
                existingDownload.Source == DownloadSource.Manual)
            {
                existingDownload.SubscriptionId = request.SubscriptionId.Value;
                existingDownload.Source = DownloadSource.Subscription;
                shouldUpdateExisting = true;
            }

            if (shouldUpdateExisting)
            {
                await db.SaveChangesAsync();
            }

            return Ok(new
            {
                message = "Download already exists",
                hash = normalizedHash,
                alreadyExists = true
            });
        }

        // Determine source: subscription-triggered or manual
        var isSubscriptionDownload = request.SubscriptionId.HasValue && request.SubscriptionId.Value > 0;
        var downloadSource = isSubscriptionDownload ? DownloadSource.Subscription : DownloadSource.Manual;

        bool success;
        try
        {
            success = await _qbittorrentService.AddTorrentWithTrackingAsync(
                torrentInput,
                normalizedHash,
                request.Title,
                0,
                downloadSource,
                animeTitle: request.AnimeTitle);
        }
        catch (QBittorrentUnavailableException ex)
        {
            _logger.LogWarning(ex, "qBittorrent unavailable while pushing download {Hash}", normalizedHash);
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

        int subscriptionId;
        if (isSubscriptionDownload)
        {
            subscriptionId = request.SubscriptionId!.Value;
        }
        else
        {
            subscriptionId = await EnsureManualSubscriptionAsync(db);
        }

        var download = new DownloadHistoryEntity
        {
            SubscriptionId = subscriptionId,
            TorrentUrl = torrentInput,
            TorrentHash = normalizedHash,
            Title = request.Title,
            Status = DownloadStatus.Pending,
            Source = downloadSource,
            AnimeBangumiId = animeBangumiId,
            AnimeMikanBangumiId = animeMikanBangumiId,
            AnimeTitle = animeTitle,
            PublishedAt = DateTime.UtcNow,
            DiscoveredAt = DateTime.UtcNow,
            DownloadedAt = DateTime.UtcNow,
            Progress = 0
        };

        db.DownloadHistory.Add(download);
        await db.SaveChangesAsync();

        _logger.LogInformation("Download added: Hash={Hash}, Source={Source}, SubscriptionId={SubId}",
            normalizedHash, downloadSource, subscriptionId);
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

            // Return all download sources (manual + subscription), deduplicated by normalized hash.
            var historyCandidates = await db.DownloadHistory
                .Where(d => !string.IsNullOrWhiteSpace(d.TorrentHash))
                .OrderByDescending(d => d.LastSyncedAt ?? d.DownloadedAt ?? d.DiscoveredAt)
                .ToListAsync();

            var latestByHash = historyCandidates
                .GroupBy(d => NormalizeHash(d.TorrentHash), StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(d => d.LastSyncedAt ?? d.DownloadedAt ?? d.DiscoveredAt)
                    .First())
                .OrderByDescending(d => d.LastSyncedAt ?? d.DownloadedAt ?? d.DiscoveredAt)
                .ToList();

            var response = latestByHash.Select(d =>
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
                    numLeechers,
                    source = d.Source.ToString().ToLowerInvariant()
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

    private static int? TryParsePositiveInt(string? value)
    {
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : null;
    }

    private async Task<JsonElement?> TryGetBangumiSubjectDetailAsync(int? bangumiId)
    {
        if (!bangumiId.HasValue)
        {
            return null;
        }

        try
        {
            var detail = await _bangumiClient.GetSubjectDetailAsync(bangumiId.Value);
            return detail.Clone();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Bangumi subject detail for BangumiId {BangumiId}", bangumiId.Value);
            return null;
        }
    }

    private static int? TryGetExpectedEpisodeCount(JsonElement? bangumiDetail)
    {
        if (!bangumiDetail.HasValue)
        {
            return null;
        }

        if (TryReadPositiveInt(bangumiDetail.Value, "eps", out var expected))
        {
            return expected;
        }

        if (TryReadPositiveInt(bangumiDetail.Value, "total_episodes", out expected))
        {
            return expected;
        }

        return null;
    }

    private static bool IsMovieOrSingleEpisodeWork(JsonElement? bangumiDetail, int? expectedEpisodes)
    {
        if (expectedEpisodes == 1)
        {
            return true;
        }

        if (!bangumiDetail.HasValue)
        {
            return false;
        }

        if (TryReadString(bangumiDetail.Value, "platform", out var platform) &&
            IsMoviePlatform(platform))
        {
            return true;
        }

        if (TryReadPositiveInt(bangumiDetail.Value, "total_episodes", out var totalEpisodes) &&
            totalEpisodes == 1)
        {
            return true;
        }

        if (TryReadPositiveInt(bangumiDetail.Value, "eps", out var eps) &&
            eps == 1)
        {
            return true;
        }

        return false;
    }

    private static bool IsMoviePlatform(string platform)
    {
        var normalized = platform.Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        return normalized.Contains("\u5267\u573a\u7248", StringComparison.Ordinal) ||
               normalized.Contains("\u5287\u5834\u7248", StringComparison.Ordinal) ||
               normalized.Contains("movie", StringComparison.Ordinal) ||
               normalized.Contains("film", StringComparison.Ordinal) ||
               normalized.Contains("theatrical", StringComparison.Ordinal) ||
               normalized.Contains("cinema", StringComparison.Ordinal);
    }

    private static void ApplyMovieOrSingleEpisodeValidation(MikanFeedResponse feed)
    {
        foreach (var item in feed.Items)
        {
            if (item.IsCollection)
            {
                continue;
            }

            item.Episode = 1;
        }

        feed.EpisodeOffset = 0;
    }

    private async Task<bool> TryApplyBangumiEpisodeMapNormalizationAsync(
        MikanFeedResponse feed,
        int bangumiId,
        int? expectedEpisodes)
    {
        var sortToEpisodeMap = await TryGetBangumiAbsoluteEpisodeMapAsync(bangumiId);
        if (sortToEpisodeMap == null || sortToEpisodeMap.Count == 0)
        {
            return false;
        }

        return ApplyBangumiEpisodeMapNormalization(feed, sortToEpisodeMap, expectedEpisodes);
    }

    private async Task<Dictionary<int, int>?> TryGetBangumiAbsoluteEpisodeMapAsync(int bangumiId)
    {
        try
        {
            const int pageSize = 100;
            var map = new Dictionary<int, int>();
            var offset = 0;
            var pages = 0;

            while (pages < 5)
            {
                var page = await _bangumiClient.GetSubjectEpisodesAsync(bangumiId, pageSize, offset);
                if (page.ValueKind != JsonValueKind.Array || page.GetArrayLength() == 0)
                {
                    break;
                }

                foreach (var item in page.EnumerateArray())
                {
                    if (!TryReadPositiveInt(item, "sort", out var absoluteEpisode))
                    {
                        continue;
                    }

                    if (!TryReadPositiveInt(item, "ep", out var seasonEpisode))
                    {
                        continue;
                    }

                    map[absoluteEpisode] = seasonEpisode;
                }

                if (page.GetArrayLength() < pageSize)
                {
                    break;
                }

                pages++;
                offset += pageSize;
            }

            return map.Count > 0 ? map : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Bangumi episodes for BangumiId {BangumiId}", bangumiId);
            return null;
        }
    }

    private static bool ApplyBangumiEpisodeMapNormalization(
        MikanFeedResponse feed,
        IReadOnlyDictionary<int, int> sortToEpisodeMap,
        int? expectedEpisodes)
    {
        var entries = feed.Items
            .Where(item => item.Episode.HasValue)
            .Select(item => new EpisodeNormalizationEntry(
                item,
                item.Episode!.Value,
                ResolveSeasonScopedEpisode(item.Title, item.Episode!.Value)))
            .ToList();

        if (entries.Count == 0)
        {
            return false;
        }

        var seasonScopedMax = entries
            .Where(entry => entry.SeasonScopedEpisode.HasValue)
            .Select(entry => entry.SeasonScopedEpisode!.Value)
            .DefaultIfEmpty(0)
            .Max();

        var mapSeasonMax = sortToEpisodeMap.Values.DefaultIfEmpty(0).Max();
        var thresholdBase = new[] { seasonScopedMax, expectedEpisodes ?? 0, mapSeasonMax }.Max();
        var absoluteTriggerThreshold = thresholdBase + 1;

        var offsets = new List<int>();
        var normalizedCount = 0;

        foreach (var entry in entries)
        {
            if (entry.SeasonScopedEpisode.HasValue)
            {
                continue;
            }

            if (entry.Episode <= absoluteTriggerThreshold)
            {
                continue;
            }

            if (!sortToEpisodeMap.TryGetValue(entry.Episode, out var seasonEpisode) || seasonEpisode <= 0)
            {
                continue;
            }

            if (entry.Episode == seasonEpisode)
            {
                continue;
            }

            entry.Item.Episode = seasonEpisode;
            normalizedCount++;

            var diff = entry.Episode - seasonEpisode;
            if (diff > 0)
            {
                offsets.Add(diff);
            }
        }

        if (normalizedCount == 0)
        {
            return false;
        }

        if (offsets.Count > 0)
        {
            feed.EpisodeOffset = offsets
                .GroupBy(value => value)
                .OrderByDescending(group => group.Count())
                .ThenByDescending(group => group.Key)
                .Select(group => group.Key)
                .First();
        }

        return true;
    }

    private async Task<int?> TryResolveOffsetFromBangumiRelationsAsync(int bangumiId)
    {
        try
        {
            var visited = new HashSet<int> { bangumiId };
            var queue = new Queue<int>();
            queue.Enqueue(bangumiId);

            var totalOffset = 0;
            var depthGuard = 0;

            while (queue.Count > 0 && depthGuard < 12)
            {
                depthGuard++;
                var currentSubjectId = queue.Dequeue();
                var relatedSubjects = await _bangumiClient.GetSubjectRelationsAsync(currentSubjectId);
                if (relatedSubjects.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var relationItem in relatedSubjects.EnumerateArray())
                {
                    if (!TryReadPositiveInt(relationItem, "id", out var relatedSubjectId))
                    {
                        continue;
                    }

                    if (!TryReadPositiveInt(relationItem, "type", out var relatedType) || relatedType != 2)
                    {
                        continue;
                    }

                    if (!TryReadString(relationItem, "relation", out var relationName) ||
                        !IsPrequelRelation(relationName))
                    {
                        continue;
                    }

                    if (!visited.Add(relatedSubjectId))
                    {
                        continue;
                    }

                    queue.Enqueue(relatedSubjectId);

                    var detail = await _bangumiClient.GetSubjectDetailAsync(relatedSubjectId);
                    var episodes = TryGetExpectedEpisodeCount(detail);
                    if (episodes.HasValue)
                    {
                        totalOffset += episodes.Value;
                    }
                }
            }

            return totalOffset > 0 ? totalOffset : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve Bangumi prequel offset for BangumiId {BangumiId}", bangumiId);
            return null;
        }
    }

    private static bool IsPrequelRelation(string relationName)
    {
        var normalized = relationName.Trim().ToLowerInvariant();
        return normalized.Contains("prequel", StringComparison.Ordinal) ||
               normalized.Contains("\u524d\u4f20", StringComparison.Ordinal) ||
               normalized.Contains("\u524d\u4f5c", StringComparison.Ordinal);
    }

    private async Task<int?> TryResolveOffsetFromAniListJikanAsync(string searchTitle)
    {
        try
        {
            var media = await _aniListClient.SearchAnimeSeasonDataAsync(searchTitle);
            if (!media.HasValue)
            {
                return null;
            }

            var offset = await SumAniListPrequelEpisodesAsync(media.Value);
            return offset > 0 ? offset : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve AniList/Jikan offset for title {Title}", searchTitle);
            return null;
        }
    }

    private async Task<int> SumAniListPrequelEpisodesAsync(JsonElement rootMedia)
    {
        var queue = new Queue<AniListRelationNode>(GetAniListPrequelNodes(rootMedia));
        if (queue.Count == 0)
        {
            return 0;
        }

        var visitedAniListIds = new HashSet<int>();
        var totalOffset = 0;
        var depthGuard = 0;

        while (queue.Count > 0 && depthGuard < 16)
        {
            depthGuard++;
            var node = queue.Dequeue();

            if (node.AniListId > 0 && !visitedAniListIds.Add(node.AniListId))
            {
                continue;
            }

            var episodes = node.Episodes;
            if (!episodes.HasValue || episodes.Value <= 0)
            {
                episodes = await TryGetEpisodesFromJikanAsync(node.MalId);
            }

            if (episodes.HasValue && episodes.Value > 0)
            {
                totalOffset += episodes.Value;
            }

            if (node.AniListId <= 0)
            {
                continue;
            }

            var prequelMedia = await _aniListClient.GetAnimeSeasonDataByIdAsync(node.AniListId);
            if (!prequelMedia.HasValue)
            {
                continue;
            }

            foreach (var prequelNode in GetAniListPrequelNodes(prequelMedia.Value))
            {
                if (prequelNode.AniListId <= 0 || visitedAniListIds.Contains(prequelNode.AniListId))
                {
                    continue;
                }

                queue.Enqueue(prequelNode);
            }
        }

        return totalOffset;
    }

    private static IEnumerable<AniListRelationNode> GetAniListPrequelNodes(JsonElement media)
    {
        if (!media.TryGetProperty("relations", out var relations) ||
            !relations.TryGetProperty("edges", out var edges) ||
            edges.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var edge in edges.EnumerateArray())
        {
            if (!TryReadString(edge, "relationType", out var relationType) ||
                !relationType.Equals("PREQUEL", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!edge.TryGetProperty("node", out var node) || node.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!TryReadString(node, "type", out var nodeType) ||
                !nodeType.Equals("ANIME", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var aniListId = TryReadPositiveInt(node, "id", out var parsedAniListId) ? parsedAniListId : 0;
            var malId = TryReadPositiveInt(node, "idMal", out var parsedMalId) ? parsedMalId : (int?)null;
            var episodes = TryReadPositiveInt(node, "episodes", out var parsedEpisodes) ? parsedEpisodes : (int?)null;

            if (aniListId > 0 || malId.HasValue)
            {
                yield return new AniListRelationNode(aniListId, malId, episodes);
            }
        }
    }

    private async Task<int?> TryGetEpisodesFromJikanAsync(int? malId)
    {
        if (!malId.HasValue || malId.Value <= 0)
        {
            return null;
        }

        var detail = await _jikanClient.GetAnimeDetailAsync(malId.Value);
        if (!detail.HasValue)
        {
            return null;
        }

        return TryReadPositiveInt(detail.Value, "episodes", out var episodes) ? episodes : null;
    }

    private static string? TryResolveAniListSearchTitle(JsonElement? bangumiDetail, MikanFeedResponse feed)
    {
        if (bangumiDetail.HasValue)
        {
            if (TryReadString(bangumiDetail.Value, "name", out var name) && !string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            if (TryReadString(bangumiDetail.Value, "name_cn", out var nameCn) && !string.IsNullOrWhiteSpace(nameCn))
            {
                return nameCn;
            }
        }

        var latestTitle = feed.Items
            .OrderByDescending(item => item.PublishedAt)
            .Select(item => item.Title)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        if (string.IsNullOrWhiteSpace(latestTitle))
        {
            return null;
        }

        var cleaned = Regex.Replace(latestTitle, @"[\[\(【].*?[\]\)】]", " ");
        cleaned = Regex.Replace(cleaned, @"\b(?:S\d{1,2}E\d{1,3}|EP?\s*\d{1,3})\b", " ", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\s{2,}", " ").Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    private static bool ApplyExplicitOffsetNormalization(
        MikanFeedResponse feed,
        int offset,
        int? expectedEpisodes)
    {
        if (offset <= 0)
        {
            return false;
        }

        var entries = feed.Items
            .Where(item => item.Episode.HasValue)
            .Select(item => new EpisodeNormalizationEntry(
                item,
                item.Episode!.Value,
                ResolveSeasonScopedEpisode(item.Title, item.Episode!.Value)))
            .ToList();
        if (entries.Count == 0)
        {
            return false;
        }

        var seasonScopedMax = entries
            .Where(entry => entry.SeasonScopedEpisode.HasValue)
            .Select(entry => entry.SeasonScopedEpisode!.Value)
            .DefaultIfEmpty(0)
            .Max();

        var trigger = Math.Max(seasonScopedMax, expectedEpisodes ?? 0) + 1;
        var upperBound = Math.Max(seasonScopedMax, expectedEpisodes ?? 0);
        if (upperBound <= 0)
        {
            upperBound = offset + 64;
        }

        var normalizedCount = 0;
        foreach (var entry in entries)
        {
            if (entry.SeasonScopedEpisode.HasValue)
            {
                continue;
            }

            if (entry.Episode <= trigger)
            {
                continue;
            }

            var normalized = entry.Episode - offset;
            if (normalized <= 0 || normalized > upperBound + 2)
            {
                continue;
            }

            entry.Item.Episode = normalized;
            normalizedCount++;
        }

        if (normalizedCount == 0)
        {
            return false;
        }

        feed.EpisodeOffset = offset;
        return true;
    }

    private static void ApplyEpisodeOffsetNormalization(MikanFeedResponse feed, int? expectedEpisodes)
    {
        var entries = feed.Items
            .Where(item => item.Episode.HasValue)
            .Select(item => new EpisodeNormalizationEntry(
                item,
                item.Episode!.Value,
                ResolveSeasonScopedEpisode(item.Title, item.Episode!.Value)))
            .ToList();

        if (entries.Count == 0)
        {
            return;
        }

        var seasonScopedEpisodes = entries
            .Where(e => e.SeasonScopedEpisode.HasValue)
            .Select(e => e.SeasonScopedEpisode!.Value)
            .Distinct()
            .OrderBy(value => value)
            .ToList();

        // Mixed numbering case: some groups use S02E04 while others use EP32.
        if (seasonScopedEpisodes.Count > 0 &&
            TryResolveMixedNumberingOffset(entries, seasonScopedEpisodes, out var mixedOffset))
        {
            var normalizedCount = ApplyOffsetToAbsoluteEpisodes(entries, mixedOffset, seasonScopedEpisodes.Max());
            if (normalizedCount > 0)
            {
                feed.EpisodeOffset = mixedOffset;
            }
            return;
        }

        var episodeNumbers = entries
            .Select(e => e.Episode)
            .OrderBy(value => value)
            .ToList();
        var offset = ResolveLegacyOffset(feed.SeasonName, episodeNumbers, expectedEpisodes);
        if (offset <= 0)
        {
            return;
        }

        foreach (var entry in entries)
        {
            var normalized = entry.Episode - offset;
            if (normalized > 0)
            {
                entry.Item.Episode = normalized;
            }
        }

        feed.EpisodeOffset = offset;
    }

    private static bool TryReadPositiveInt(JsonElement source, string propertyName, out int value)
    {
        value = 0;
        if (!source.TryGetProperty(propertyName, out var element))
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt32(out value) &&
            value > 0)
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.String &&
            int.TryParse(element.GetString(), out value) &&
            value > 0)
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryReadString(JsonElement source, string propertyName, out string value)
    {
        value = string.Empty;
        if (!source.TryGetProperty(propertyName, out var element))
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }

        if (element.ValueKind == JsonValueKind.Number)
        {
            value = element.GetRawText();
            return !string.IsNullOrWhiteSpace(value);
        }

        return false;
    }

    private static int ResolveLegacyOffset(string? seasonName, IReadOnlyList<int> sortedEpisodes, int? expectedEpisodes)
    {
        if (sortedEpisodes.Count == 0)
        {
            return 0;
        }

        var minEpisode = sortedEpisodes[0];
        var maxEpisode = sortedEpisodes[^1];
        if (minEpisode <= 1)
        {
            return 0;
        }

        var span = maxEpisode - minEpisode + 1;
        var seasonNumber = TryExtractSeasonNumber(seasonName) ?? 1;

        if (expectedEpisodes.HasValue && expectedEpisodes.Value > 0)
        {
            var expected = expectedEpisodes.Value;
            if (maxEpisode > expected && span <= expected + 2)
            {
                return minEpisode - 1;
            }

            return 0;
        }

        // Fallback heuristic when Bangumi metadata is unavailable.
        if (seasonNumber > 1 && span <= 30)
        {
            return minEpisode - 1;
        }

        return 0;
    }

    private static bool TryResolveMixedNumberingOffset(
        IReadOnlyList<EpisodeNormalizationEntry> entries,
        IReadOnlyCollection<int> seasonScopedEpisodes,
        out int offset)
    {
        offset = 0;
        var seasonScopedSet = new HashSet<int>(seasonScopedEpisodes);
        var seasonScopedMax = seasonScopedEpisodes.Max();
        var nonSeasonEpisodes = entries
            .Where(e => !e.SeasonScopedEpisode.HasValue)
            .Select(e => e.Episode)
            .Distinct()
            .OrderBy(value => value)
            .ToList();

        if (nonSeasonEpisodes.Count == 0)
        {
            return false;
        }

        // Mixed numbering should be judged by season-scoped anchors first.
        // Bangumi "eps" can sometimes be cumulative / stale and should not suppress offset detection.
        var absoluteTriggerThreshold = seasonScopedMax + 2;
        if (!nonSeasonEpisodes.Any(value => value > absoluteTriggerThreshold))
        {
            return false;
        }

        var candidateOffsets = new HashSet<int>();
        foreach (var absoluteEpisode in nonSeasonEpisodes)
        {
            foreach (var relativeEpisode in seasonScopedSet)
            {
                var diff = absoluteEpisode - relativeEpisode;
                if (diff > 0 && diff <= 500)
                {
                    candidateOffsets.Add(diff);
                }
            }
        }

        if (candidateOffsets.Count == 0)
        {
            return false;
        }

        var upperBound = seasonScopedMax + 2;
        var bestOffset = 0;
        var bestScore = 0;

        foreach (var candidate in candidateOffsets)
        {
            var score = 0;
            foreach (var episode in nonSeasonEpisodes)
            {
                var normalized = episode - candidate;
                if (normalized <= 0)
                {
                    continue;
                }

                if (normalized <= upperBound)
                {
                    score += 1;
                }

                if (seasonScopedSet.Contains(normalized))
                {
                    score += 2;
                }
            }

            if (score > bestScore || (score == bestScore && candidate > bestOffset))
            {
                bestScore = score;
                bestOffset = candidate;
            }
        }

        if (bestOffset <= 0 || bestScore < 2)
        {
            return false;
        }

        offset = bestOffset;
        return true;
    }

    private static int ApplyOffsetToAbsoluteEpisodes(
        IReadOnlyList<EpisodeNormalizationEntry> entries,
        int offset,
        int fallbackThreshold)
    {
        var threshold = fallbackThreshold + 2;
        var upperBound = fallbackThreshold + 2;
        var normalizedCount = 0;

        foreach (var entry in entries)
        {
            if (entry.SeasonScopedEpisode.HasValue)
            {
                continue;
            }

            if (entry.Episode <= threshold)
            {
                continue;
            }

            var normalized = entry.Episode - offset;
            if (normalized > 0 && normalized <= upperBound)
            {
                entry.Item.Episode = normalized;
                normalizedCount++;
            }
        }

        return normalizedCount;
    }

    private static int? ResolveSeasonScopedEpisode(string? title, int episode)
    {
        var explicitSeasonScoped = TryExtractSeasonScopedEpisode(title);
        return explicitSeasonScoped;
    }

    private static int? TryExtractSeasonScopedEpisode(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var seasonEpisodeMatch = Regex.Match(
            title,
            @"\bs\s*\d{1,2}\s*e\s*0*(\d{1,3})\b",
            RegexOptions.IgnoreCase);
        if (seasonEpisodeMatch.Success &&
            int.TryParse(seasonEpisodeMatch.Groups[1].Value, out var seasonEpisode) &&
            seasonEpisode > 0)
        {
            return seasonEpisode;
        }

        var chineseSeasonEpisodeMatch = Regex.Match(
            title,
            @"\u7b2c\s*[0-9\u4e00\u4e8c\u4e09\u56db\u4e94\u516d\u4e03\u516b\u4e5d\u5341]+\s*\u5b63[^\d]{0,16}(?:\u7b2c\s*)?0*(\d{1,3})\s*(?:\u8bdd|\u8a71|\u96c6)",
            RegexOptions.IgnoreCase);
        if (chineseSeasonEpisodeMatch.Success &&
            int.TryParse(chineseSeasonEpisodeMatch.Groups[1].Value, out seasonEpisode) &&
            seasonEpisode > 0)
        {
            return seasonEpisode;
        }

        return null;
    }

    /// <summary>
    /// Scrapes https://bgm.tv/subject/{bangumiId} and extracts from ul.prg_list:
    ///   - The first li's episode number → offset = number - 1
    ///   - The span.tip number → totalEpisodes (optional)
    /// Returns null on failure (silently degrades to next method).
    /// </summary>
    private async Task<(int offset, int? totalEpisodes)?> TryGetBangumiPrgListInfoAsync(int bangumiId)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var html = await client.GetStringAsync($"https://bgm.tv/subject/{bangumiId}");

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Find the first li under ul.prg_list
            var firstLi = doc.DocumentNode
                .SelectSingleNode("//ul[contains(@class,'prg_list')]/li[1]");
            if (firstLi == null)
            {
                return null;
            }

            var firstEpText = firstLi.InnerText;
            var firstEpMatch = Regex.Match(firstEpText, @"\b(\d+)\b");
            if (!firstEpMatch.Success || !int.TryParse(firstEpMatch.Groups[1].Value, out var firstEp))
            {
                return null;
            }

            var offset = firstEp - 1;
            if (offset <= 0)
            {
                // Season starts at ep1, no offset needed
                return null;
            }

            // Optionally extract total episode count from span.tip
            int? totalEpisodes = null;
            var tipSpan = doc.DocumentNode
                .SelectSingleNode("//ul[contains(@class,'prg_list')]//span[contains(@class,'tip')]");
            if (tipSpan != null)
            {
                var tipMatch = Regex.Match(tipSpan.InnerText, @"\b(\d+)\b");
                if (tipMatch.Success && int.TryParse(tipMatch.Groups[1].Value, out var total) && total > 0)
                {
                    totalEpisodes = total;
                }
            }

            _logger.LogInformation(
                "PrgList: BangumiId={Id} firstEp={First} offset={Offset} totalEpisodes={Total}",
                bangumiId, firstEp, offset, totalEpisodes);

            return (offset, totalEpisodes);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "TryGetBangumiPrgListInfoAsync failed for BangumiId {Id}", bangumiId);
            return null;
        }
    }

    private sealed record EpisodeNormalizationEntry(
        ParsedRssItem Item,
        int Episode,
        int? SeasonScopedEpisode);

    private sealed record AniListRelationNode(
        int AniListId,
        int? MalId,
        int? Episodes);

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

        if (!Regex.IsMatch(defaultSeason.MikanBangumiId, @"^\d+$"))
        {
            _logger.LogInformation(
                "Skip caching non-numeric MikanBangumiId for BangumiId={BangumiId}, MikanId={MikanId}",
                bangumiId,
                defaultSeason.MikanBangumiId);
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
