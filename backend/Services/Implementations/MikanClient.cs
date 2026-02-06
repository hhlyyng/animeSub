using System.Xml.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using backend.Models.Configuration;
using backend.Models.Mikan;
using backend.Models.Dtos;
using backend.Services.Interfaces;
using HtmlAgilityPack;

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
    private readonly ITorrentTitleParser _titleParser;

    // Regex to extract info hash from magnet link or torrent URL
    [GeneratedRegex(@"[a-fA-F0-9]{40}", RegexOptions.Compiled)]
    private static partial Regex HashRegex();

    public MikanClient(
        HttpClient httpClient,
        ILogger<MikanClient> logger,
        IOptions<MikanConfiguration> config,
        ITorrentTitleParser titleParser)
    {
        _httpClient = httpClient;
        _logger = logger;
        _config = config.Value;
        _titleParser = titleParser;

        // Set base address and timeout
        var baseUrl = _config.BaseUrl;
        if (!baseUrl.EndsWith('/'))
            baseUrl += '/';
        _httpClient.BaseAddress = new Uri(baseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(_config.TimeoutSeconds);
    }

    public string BuildRssUrl(string mikanBangumiId, string? subgroupId = null)
    {
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

    public async Task<MikanSearchResult?> SearchAnimeAsync(string title)
    {
        var searchUrl = $"Home/Search?searchstr={Uri.EscapeDataString(title)}";

        _logger.LogInformation("Searching Mikan HTML for: {Title}, URL: {SearchUrl}", title, searchUrl);

        try
        {
            var response = await _httpClient.GetAsync(searchUrl);
            response.EnsureSuccessStatusCode();
            _logger.LogInformation("Mikan search response status: {Status}", response.StatusCode);

            var htmlContent = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("Mikan search HTML content length: {Length} chars", htmlContent.Length);

            var result = ParseSearchHtml(htmlContent, title);

            if (result != null)
            {
                _logger.LogInformation("Search result found: AnimeTitle={AnimeTitle}, SeasonsCount={SeasonsCount}, DefaultSeason={DefaultSeason}",
                    result.AnimeTitle, result.Seasons.Count, result.DefaultSeason);
            }
            else
            {
                _logger.LogWarning("Search result is null for: {Title}", title);
            }

            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to search Mikan HTML for title: {Title}", title);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing Mikan search result for: {Title}", title);
            return null;
        }
    }

    private MikanSearchResult? ParseSearchHtml(string htmlContent, string searchTerm)
    {
        _logger.LogInformation("Starting HTML parsing for search term: {SearchTerm}", searchTerm);

        var doc = new HtmlDocument();
        doc.LoadHtml(htmlContent);
        _logger.LogInformation("HTML loaded successfully");

        // Find all Bangumi links in search results
        var bangumiLinks = doc.DocumentNode.SelectNodes("//a[contains(@href, '/Home/Bangumi/')]");

        if (bangumiLinks == null || bangumiLinks.Count == 0)
        {
            _logger.LogWarning("No Bangumi links found in search results");
            return null;
        }

        _logger.LogInformation("Found {Count} Bangumi links", bangumiLinks.Count);

        var searchResult = new MikanSearchResult
        {
            AnimeTitle = searchTerm
        };

        var seenIds = new HashSet<string>();

        foreach (var bangumiLink in bangumiLinks)
        {
            var href = bangumiLink.GetAttributeValue("href", "");
            var bangumiId = ExtractBangumiIdFromBangumiUrl(href);

            if (!string.IsNullOrEmpty(bangumiId) && !seenIds.Contains(bangumiId))
            {
                seenIds.Add(bangumiId);

                string seasonName = "Season 1";
                int year = 0;

                // Try to detect season and year from link's parent elements
                var liElement = bangumiLink.Ancestors("li").FirstOrDefault();
                if (liElement != null)
                {
                    // Check for title text in the same list item
                    var titleSpan = liElement.SelectSingleNode(".//span[contains(@class, 'an-title')]");
                    if (titleSpan != null)
                    {
                        var titleText = titleSpan.InnerText;

                        if (titleText.ToLowerInvariant().Contains("season 2") ||
                            titleText.ToLowerInvariant().Contains("s2") ||
                            titleText.ToLowerInvariant().Contains("第二季") ||
                            titleText.ToLowerInvariant().Contains("2nd season"))
                        {
                            seasonName = "Season 2";
                        }

                        var yearMatch = Regex.Match(titleText, @"\b(20\d{2})\b");
                        if (yearMatch.Success && int.TryParse(yearMatch.Groups[1].Value, out var yearNum))
                        {
                            year = yearNum;
                        }
                    }
                }

                searchResult.Seasons.Add(new MikanSeasonInfo
                {
                    SeasonName = seasonName,
                    MikanBangumiId = bangumiId,
                    Year = year
                });
            }
        }

        if (searchResult.Seasons.Count > 0)
        {
            searchResult.DefaultSeason = 0;
            _logger.LogInformation("Found {Count} seasons via Bangumi links", searchResult.Seasons.Count);
            return searchResult;
        }

        _logger.LogWarning("No valid seasons found");
        return null;
    }

    private string? ExtractBangumiIdFromBangumiUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        // Extract numeric bangumiId from URL like: /Home/Bangumi/229
        var match = Regex.Match(url, @"/Home/Bangumi/(\d+)");
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        return null;
    }

    private string? ExtractBangumiIdFromUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        // Extract bangumiId from URL like: /RSS/Bangumi?bangumiId=xxx
        var match = Regex.Match(url, @"bangumiId=([^&]+)");
        if (match.Success)
        {
            return match.Groups[1].Value;
        }
        
        return null;
    }

    public async Task<MikanFeedResponse> GetParsedFeedAsync(string mikanBangumiId)
    {
        _logger.LogInformation("Getting parsed feed for Mikan ID: {MikanId}", mikanBangumiId);
        
        var feed = await GetAnimeFeedAsync(mikanBangumiId);
        _logger.LogInformation("Retrieved RSS feed: Title={Title}, ItemsCount={Count}", feed.Title, feed.Items.Count);
        
        var response = new MikanFeedResponse
        {
            SeasonName = feed.Title,
            Items = new List<ParsedRssItem>(),
            AvailableSubgroups = new List<string>(),
            AvailableResolutions = new List<string>(),
            AvailableSubtitleTypes = new List<string>()
        };

        var subgroupsSet = new HashSet<string>();
        var resolutionsSet = new HashSet<string>();
        var subtitleTypesSet = new HashSet<string>();

        foreach (var item in feed.Items.OrderByDescending(i => i.PublishedAt))
        {
            var parsed = _titleParser.ParseTitle(item.Title);
            _logger.LogDebug("Parsed item: Title={Title}, Resolution={Res}, Subgroup={Sub}, Episode={Ep}", 
                item.Title, parsed.Resolution, parsed.Subgroup, parsed.Episode);
            
            var parsedItem = new ParsedRssItem
            {
                Title = item.Title,
                TorrentUrl = item.TorrentUrl,
                MagnetLink = item.MagnetLink ?? string.Empty,
                TorrentHash = item.TorrentHash,
                FileSize = item.FileSize ?? 0,
                FormattedSize = FormatFileSize(item.FileSize ?? 0),
                PublishedAt = item.PublishedAt,
                Resolution = parsed.Resolution,
                Subgroup = parsed.Subgroup,
                SubtitleType = parsed.SubtitleType,
                Episode = parsed.Episode
            };

            response.Items.Add(parsedItem);

            // Collect available filter options
            if (!string.IsNullOrEmpty(parsed.Subgroup))
            {
                subgroupsSet.Add(parsed.Subgroup);
            }
            if (!string.IsNullOrEmpty(parsed.Resolution))
            {
                resolutionsSet.Add(parsed.Resolution);
            }
            if (!string.IsNullOrEmpty(parsed.SubtitleType))
            {
                subtitleTypesSet.Add(parsed.SubtitleType);
            }
        }

        response.AvailableSubgroups = subgroupsSet.ToList();
        response.AvailableResolutions = resolutionsSet.ToList();
        response.AvailableSubtitleTypes = subtitleTypesSet.ToList();

        _logger.LogInformation("Parsed feed '{Title}' with {Count} items, {Subgroups} subgroups, {Resolutions} resolutions, {SubtitleTypes} subtitle types",
            feed.Title, response.Items.Count, response.AvailableSubgroups.Count, response.AvailableResolutions.Count, response.AvailableSubtitleTypes.Count);

        return response;
    }

    private static string FormatFileSize(long bytes)
    {
        const long KB = 1024;
        const long MB = KB * 1024;
        const long GB = MB * 1024;

        if (bytes < KB)
            return bytes + " B";
        if (bytes < MB)
            return ((double)bytes / KB).ToString("F1") + " KB";
        if (bytes < GB)
            return ((double)bytes / MB).ToString("F1") + " MB";
        
        return ((double)bytes / GB).ToString("F2") + " GB";
    }
}