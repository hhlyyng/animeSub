using Microsoft.AspNetCore.Mvc;
using backend.Models.Dtos;
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

    public MikanController(
        IMikanClient mikanClient,
        IQBittorrentService qbittorrentService,
        ILogger<MikanController> logger)
    {
        _mikanClient = mikanClient;
        _qbittorrentService = qbittorrentService;
        _logger = logger;
    }

    /// <summary>
    /// Search for an anime on Mikan and return all seasons
    /// </summary>
    /// <param name="title">Anime title to search for</param>
    /// <returns>Search result with seasons</returns>
    [HttpGet("search")]
    public async Task<ActionResult<MikanSearchResult>> Search([FromQuery] string title)
    {
        // Log the full request information
        _logger.LogInformation("=== Mikan Search API Called ===");
        _logger.LogInformation("Path: {Path}", Request.Path);
        _logger.LogInformation("QueryString: {QueryString}", Request.QueryString);
        _logger.LogInformation("Title parameter: {Title}", title);
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

        var success = await _qbittorrentService.AddTorrentAsync(
            request.MagnetLink,
            savePath: null,
            category: "anime",
            paused: false);

        if (success)
        {
            _logger.LogInformation("Successfully added torrent: {Title}", request.Title);
            return Ok(new { message = "Torrent added successfully" });
        }
        else
        {
            _logger.LogWarning("Failed to add torrent: {Title}", request.Title);
            return StatusCode(500, new { message = "Failed to add torrent to qBittorrent" });
        }
    }

    /// <summary>
    /// Check download status of torrents
    /// </summary>
    /// <returns>List of torrents in qBittorrent</returns>
    [HttpGet("torrents")]
    public async Task<ActionResult> GetTorrents()
    {
        try
        {
            var torrents = await _qbittorrentService.GetTorrentsAsync(category: "anime");
            return Ok(torrents.Select(t => new
            {
                hash = t.Hash,
                name = t.Name,
                size = t.Size,
                state = t.State,
                progress = t.Progress
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting torrents list");
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
}