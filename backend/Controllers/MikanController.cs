using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using backend.Models.Dtos;
using backend.Data;
using backend.Data.Entities;
using backend.Services.Interfaces;
using System.Diagnostics;

namespace backend.Controllers;

/// <summary>
/// API controller for Mikan anime download features
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class MikanController : ControllerBase
{
    private readonly IMikanClient _mikanClient;
    private readonly IQBittorrentService _qbittorrentService;
    private readonly ILogger<MikanController> _logger;
    private readonly IServiceProvider _serviceProvider;
    private const string ManualSubscriptionTitle = "__manual_download_tracking__";
    private const string ManualSubscriptionMikanId = "manual";
    private const int ManualSubscriptionBangumiId = -1;

    public MikanController(
        IMikanClient mikanClient,
        IQBittorrentService qbittorrentService,
        ILogger<MikanController> logger,
        IServiceProvider serviceProvider)
    {
        _mikanClient = mikanClient;
        _qbittorrentService = qbittorrentService;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Search for an anime on Mikan and return all seasons
    /// </summary>
    /// <param name="title">Anime title to search for</param>
    /// <param name="bangumiId">Optional Bangumi ID for caching the matched Mikan ID</param>
    /// <returns>Search result with seasons</returns>
    [HttpGet("search")]
    public async Task<ActionResult<MikanSearchResult>> Search([FromQuery] string title, [FromQuery] string? bangumiId = null)
    {
        // Log the full request information
        _logger.LogInformation("=== Mikan Search API Called ===");
        _logger.LogInformation("Path: {Path}", Request.Path);
        _logger.LogInformation("QueryString: {QueryString}", Request.QueryString);
        _logger.LogInformation("Title parameter: {Title}", title);
        _logger.LogInformation("BangumiId parameter: {BangumiId}", bangumiId);
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

            _logger.LogInformation("Search SUCCESS for: {Title} (elapsed: {Elapsed}ms), Seasons: {Count}", 
                title, stopwatch.ElapsedMilliseconds, result.Seasons.Count);
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
    /// <returns>Feed with parsed items and available filters</returns>
    [HttpGet("feed")]
    public async Task<ActionResult<MikanFeedResponse>> GetFeed([FromQuery] string mikanId)
    {
        if (string.IsNullOrWhiteSpace(mikanId))
        {
            return BadRequest(new { message = "Mikan Bangumi ID is required" });
        }

        try
        {
            var feed = await _mikanClient.GetParsedFeedAsync(mikanId);
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
        if (string.IsNullOrWhiteSpace(request.MagnetLink))
        {
            return BadRequest(new { message = "Magnet link is required" });
        }

        if (string.IsNullOrWhiteSpace(request.TorrentHash))
        {
            return BadRequest(new { message = "Torrent hash is required" });
        }

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimeDbContext>();

        var existingDownload = await db.DownloadHistory
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.TorrentHash == request.TorrentHash);

        if (existingDownload != null)
        {
            return Ok(new
            {
                message = "Download already exists",
                hash = request.TorrentHash,
                alreadyExists = true
            });
        }

        var success = await _qbittorrentService.AddTorrentWithTrackingAsync(
            request.MagnetLink,
            request.TorrentHash,
            request.Title,
            0,
            DownloadSource.Manual);

        if (!success)
        {
            return StatusCode(500, new { message = "Failed to add torrent to qBittorrent" });
        }

        var manualSubscriptionId = await EnsureManualSubscriptionAsync(db);

        var download = new DownloadHistoryEntity
        {
            SubscriptionId = manualSubscriptionId,
            TorrentUrl = request.MagnetLink,
            TorrentHash = request.TorrentHash,
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

        _logger.LogInformation("Manual download added: Hash={Hash}, Source={Source}", request.TorrentHash, DownloadSource.Manual);

        return Ok(new { message = "Download added successfully", hash = request.TorrentHash });
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

            var torrents = await db.DownloadHistory
                .Where(d => d.Source == DownloadSource.Manual)
                .OrderByDescending(d => d.DownloadedAt ?? d.DiscoveredAt)
                .Select(d => new
                {
                    hash = d.TorrentHash,
                    name = d.Title,
                    size = d.FileSize,
                    state = d.Status.ToString(),
                    progress = d.Progress,
                    downloadSpeed = d.DownloadSpeed,
                    eta = d.Eta,
                    numSeeds = d.NumSeeds,
                    numLeechers = d.NumLeechers
                })
                .ToListAsync();

            return Ok(torrents);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting torrents list from database");
            return StatusCode(500, new { message = "Failed to get torrents" });
        }
    }

    private static List<ParsedRssItem> ApplyFilters(
        List<ParsedRssItem> items,
        string? resolution,
        string? subgroup,
        string? subtitleType)
    {
        var filtered = items.AsEnumerable();

        if (!string.IsNullOrEmpty(resolution) && resolution.ToLowerInvariant() != "all")
        {
            filtered = filtered.Where(item => 
                item.Resolution != null && item.Resolution.Equals(resolution, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(subgroup) && subgroup.ToLowerInvariant() != "all")
        {
            filtered = filtered.Where(item =>
                item.Subgroup != null && item.Subgroup.Equals(subgroup, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(subtitleType) && subtitleType.ToLowerInvariant() != "all")
        {
            filtered = filtered.Where(item =>
                item.SubtitleType != null && item.SubtitleType.Equals(subtitleType, StringComparison.OrdinalIgnoreCase));
        }

        return filtered.OrderByDescending(i => i.PublishedAt).ToList();
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
}
