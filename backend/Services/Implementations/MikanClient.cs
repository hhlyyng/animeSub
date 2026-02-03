using System.Xml.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using backend.Models.Configuration;
using backend.Models.Mikan;
using backend.Services.Interfaces;

namespace backend.Services.Implementations;

/// <summary>
/// Client implementation for Mikan RSS service
/// Parses RSS XML feeds and extracts torrent information
/// </summary>
public partial class MikanClient : IMikanClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MikanClient> _logger;
    private readonly MikanConfiguration _config;

    // Regex to extract info hash from magnet link or torrent URL
    [GeneratedRegex(@"[a-fA-F0-9]{40}", RegexOptions.Compiled)]
    private static partial Regex HashRegex();

    public MikanClient(
        HttpClient httpClient,
        ILogger<MikanClient> logger,
        IOptions<MikanConfiguration> config)
    {
        _httpClient = httpClient;
        _logger = logger;
        _config = config.Value;

        // Set base address and timeout
        var baseUrl = _config.BaseUrl;
        if (!baseUrl.EndsWith('/'))
            baseUrl += '/';
        _httpClient.BaseAddress = new Uri(baseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(_config.TimeoutSeconds);
    }

    public string BuildRssUrl(string mikanBangumiId, string? subgroupId = null)
    {
        // Mikan RSS URL format:
        // Without subgroup: /RSS/Bangumi?bangumiId={id}
        // With subgroup: /RSS/Bangumi?bangumiId={id}&subgroupid={subgroupId}
        var url = $"RSS/Bangumi?bangumiId={mikanBangumiId}";
        if (!string.IsNullOrEmpty(subgroupId))
        {
            url += $"&subgroupid={subgroupId}";
        }
        return url;
    }

    public async Task<MikanRssFeed> GetAnimeFeedAsync(string mikanBangumiId, string? subgroupId = null)
    {
        var url = BuildRssUrl(mikanBangumiId, subgroupId);

        _logger.LogDebug("Fetching Mikan RSS: {Url}", url);

        try
        {
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            return ParseRssFeed(content);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to fetch Mikan RSS for bangumiId {BangumiId}", mikanBangumiId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing Mikan RSS for bangumiId {BangumiId}", mikanBangumiId);
            throw;
        }
    }

    private MikanRssFeed ParseRssFeed(string xmlContent)
    {
        var feed = new MikanRssFeed();
        var doc = XDocument.Parse(xmlContent);
        var channel = doc.Root?.Element("channel");

        if (channel == null)
        {
            _logger.LogWarning("Invalid RSS feed: no channel element found");
            return feed;
        }

        feed.Title = channel.Element("title")?.Value ?? string.Empty;
        feed.Description = channel.Element("description")?.Value;
        feed.Link = channel.Element("link")?.Value;

        foreach (var item in channel.Elements("item"))
        {
            var rssItem = ParseRssItem(item);
            if (rssItem != null)
            {
                feed.Items.Add(rssItem);
            }
        }

        _logger.LogDebug("Parsed RSS feed '{Title}' with {Count} items", feed.Title, feed.Items.Count);
        return feed;
    }

    private MikanRssItem? ParseRssItem(XElement item)
    {
        var title = item.Element("title")?.Value;
        if (string.IsNullOrEmpty(title))
        {
            return null;
        }

        var rssItem = new MikanRssItem
        {
            Title = title,
            Link = item.Element("link")?.Value
        };

        // Parse enclosure for torrent URL and size
        var enclosure = item.Element("enclosure");
        if (enclosure != null)
        {
            rssItem.TorrentUrl = enclosure.Attribute("url")?.Value ?? string.Empty;
            if (long.TryParse(enclosure.Attribute("length")?.Value, out var size))
            {
                rssItem.FileSize = size;
            }
        }

        // Try to extract hash from torrent URL or guid
        var torrentHash = ExtractTorrentHash(rssItem.TorrentUrl);
        if (string.IsNullOrEmpty(torrentHash))
        {
            var guid = item.Element("guid")?.Value;
            if (!string.IsNullOrEmpty(guid))
            {
                torrentHash = ExtractTorrentHash(guid);
            }
        }
        rssItem.TorrentHash = torrentHash ?? string.Empty;

        // Parse publish date
        var pubDateStr = item.Element("pubDate")?.Value;
        if (!string.IsNullOrEmpty(pubDateStr) && DateTime.TryParse(pubDateStr, out var pubDate))
        {
            rssItem.PublishedAt = pubDate.ToUniversalTime();
        }
        else
        {
            rssItem.PublishedAt = DateTime.UtcNow;
        }

        // Check for Mikan-specific elements (torrent namespace)
        // Some Mikan feeds include torrent:magnetURI
        var torrentNs = XNamespace.Get("https://mikanani.me/0.1/");
        var magnetUri = item.Element(torrentNs + "magnetURI")?.Value;
        if (!string.IsNullOrEmpty(magnetUri))
        {
            rssItem.MagnetLink = magnetUri;
            // Try to extract hash from magnet link if we don't have one yet
            if (string.IsNullOrEmpty(rssItem.TorrentHash))
            {
                rssItem.TorrentHash = ExtractTorrentHash(magnetUri) ?? string.Empty;
            }
        }

        return rssItem;
    }

    private static string? ExtractTorrentHash(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        var match = HashRegex().Match(url);
        return match.Success ? match.Value.ToUpperInvariant() : null;
    }
}
