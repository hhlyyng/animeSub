using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HtmlAgilityPack;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using backend.Data;
using backend.Data.Entities;
using backend.Models.Configuration;
using backend.Models.Dtos;
using backend.Models.Mikan;
using backend.Services.Interfaces;
using backend.Services.Utils;

namespace backend.Services.Implementations;

/// <summary>
/// Client implementation for Mikan RSS service
/// Parses RSS XML feeds and extracts torrent information
/// </summary>
public partial class MikanClient : IMikanClient
{
    private const string SearchPseudoMikanIdPrefix = "search:";

    private readonly HttpClient _httpClient;
    private readonly ILogger<MikanClient> _logger;
    private readonly MikanConfiguration _config;
    private readonly ITorrentTitleParser _titleParser;
    private readonly AnimeDbContext? _dbContext;
    private readonly TimeSpan _feedCacheTtl;

    [GeneratedRegex(@"\b(20\d{2})\b", RegexOptions.Compiled)]
    private static partial Regex YearRegex();

    [GeneratedRegex(@"(?:season\s*(\d+)|\bs\s*0?(\d+)\b|(\d+)(?:st|nd|rd|th)\s*season|\u7b2c\s*([0-9\u4e00\u4e8c\u4e09\u56db\u4e94\u516d\u4e03\u516b\u4e5d\u5341]+)\s*\u5b63)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex SeasonNumberRegex();

    [GeneratedRegex(@"^(?:\d{1,4}|(?:2160|1080|720)p|4k|x26[45]|hevc|aac|flac|av1|mkv|mp4|chs|cht|gb|big5)$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex NonSubgroupTokenRegex();

    public MikanClient(
        HttpClient httpClient,
        ILogger<MikanClient> logger,
        IOptions<MikanConfiguration> config,
        ITorrentTitleParser titleParser,
        AnimeDbContext? dbContext = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _config = config.Value;
        _titleParser = titleParser;
        _dbContext = dbContext;
        _feedCacheTtl = TimeSpan.FromMinutes(Math.Max(1, _config.FeedCacheTtlMinutes));

        var baseUrl = _config.BaseUrl;
        if (!baseUrl.EndsWith('/'))
        {
            baseUrl += "/";
        }

        _httpClient.BaseAddress = new Uri(baseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(_config.TimeoutSeconds);
    }

    public string BuildRssUrl(string mikanBangumiId, string? subgroupId = null)
    {
        if (TryResolveSearchQueryFromPseudoMikanId(mikanBangumiId, out var searchQuery))
        {
            return $"RSS/Search?searchstr={EncodeSearchQueryForMikan(searchQuery)}";
        }

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

        var enclosure = item.Element("enclosure");
        if (enclosure != null)
        {
            rssItem.TorrentUrl = enclosure.Attribute("url")?.Value ?? string.Empty;
            if (long.TryParse(enclosure.Attribute("length")?.Value, out var size))
            {
                rssItem.FileSize = size;
            }
        }

        var pubDateStr = ExtractPublishedAtRaw(item);
        if (!string.IsNullOrWhiteSpace(pubDateStr) &&
            DateTimeOffset.TryParse(pubDateStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var pubDate))
        {
            rssItem.PublishedAt = pubDate.UtcDateTime;
        }
        else
        {
            rssItem.PublishedAt = DateTime.UtcNow;
        }

        var torrentNs = XNamespace.Get("https://mikanani.me/0.1/");
        var magnetUri = item.Element(torrentNs + "magnetURI")?.Value;
        if (!string.IsNullOrEmpty(magnetUri))
        {
            rssItem.MagnetLink = magnetUri;
        }

        var guid = item.Element("guid")?.Value;
        rssItem.TorrentHash = TorrentHashHelper.ResolveHash(
            rssItem.MagnetLink,
            rssItem.TorrentUrl,
            guid) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(rssItem.TorrentHash))
        {
            _logger.LogWarning("Failed to extract torrent hash from RSS item: {Title}", rssItem.Title);
        }

        return rssItem;
    }

    private static string? ExtractPublishedAtRaw(XElement item)
    {
        var directPubDate = item.Element("pubDate")?.Value;
        if (!string.IsNullOrWhiteSpace(directPubDate))
        {
            return directPubDate;
        }

        var torrentPubDate = item
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName.Equals("pubDate", StringComparison.OrdinalIgnoreCase))
            ?.Value;

        return string.IsNullOrWhiteSpace(torrentPubDate) ? null : torrentPubDate;
    }

    public async Task<MikanSearchResult?> SearchAnimeAsync(string title)
    {
        var queries = BuildSearchQueries(title);

        foreach (var query in queries)
        {
            var searchUrl = $"Home/Search?searchstr={query.QueryValue}";
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
                    _logger.LogInformation(
                        "Search result found: AnimeTitle={AnimeTitle}, SeasonsCount={SeasonsCount}, DefaultSeason={DefaultSeason}",
                        result.AnimeTitle,
                        result.Seasons.Count,
                        result.DefaultSeason);
                    return result;
                }

                _logger.LogWarning("Search result is null for: {Title}, mode={Mode}", title, query.Mode);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Failed to search Mikan HTML for title: {Title}, mode={Mode}, trying next strategy", title, query.Mode);
                continue;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error parsing Mikan search result for: {Title}, mode={Mode}, trying next strategy", title, query.Mode);
                continue;
            }
        }

        _logger.LogWarning("All search strategies exhausted for title: {Title}", title);
        return null;
    }

    public async Task<List<MikanAnimeEntry>> SearchAnimeEntriesAsync(string title)
    {
        var queries = BuildSearchQueries(title);
        var entries = new List<MikanAnimeEntry>();
        var seenIds = new HashSet<string>();

        foreach (var query in queries)
        {
            var searchUrl = $"Home/Search?searchstr={query.QueryValue}";
            _logger.LogInformation("SearchAnimeEntries: searching Mikan with URL: {SearchUrl}", searchUrl);

            try
            {
                var response = await _httpClient.GetAsync(searchUrl);
                response.EnsureSuccessStatusCode();

                var htmlContent = await response.Content.ReadAsStringAsync();
                var doc = new HtmlDocument();
                doc.LoadHtml(htmlContent);

                var anUl = doc.DocumentNode.SelectSingleNode("//ul[contains(@class, 'an-ul')]");
                if (anUl == null)
                {
                    _logger.LogInformation("SearchAnimeEntries: no an-ul found for query mode={Mode}", query.Mode);
                    continue;
                }

                var liNodes = anUl.SelectNodes("li");
                if (liNodes == null || liNodes.Count == 0)
                {
                    continue;
                }

                foreach (var li in liNodes)
                {
                    var bangumiLink = li.SelectSingleNode(".//a[contains(@href, '/Home/Bangumi/')]");
                    if (bangumiLink == null)
                    {
                        continue;
                    }

                    var href = bangumiLink.GetAttributeValue("href", string.Empty);
                    var mikanBangumiId = ExtractBangumiIdFromBangumiUrl(href);
                    if (string.IsNullOrEmpty(mikanBangumiId) || !seenIds.Add(mikanBangumiId))
                    {
                        continue;
                    }

                    // Extract title from .an-text element's title attribute, fallback to inner text
                    var anText = li.SelectSingleNode(".//*[contains(@class, 'an-text')]");
                    var entryTitle = anText?.GetAttributeValue("title", null)
                        ?? anText?.InnerText?.Trim()
                        ?? HtmlEntity.DeEntitize(bangumiLink.InnerText?.Trim() ?? string.Empty);

                    // Extract image URL from span.b-lazy[data-src] (Mikan uses lazy-loading)
                    var imageUrl = string.Empty;
                    var lazySpan = li.SelectSingleNode(".//span[contains(@class, 'b-lazy')]");
                    if (lazySpan != null)
                    {
                        var dataSrc = lazySpan.GetAttributeValue("data-src", string.Empty);
                        if (!string.IsNullOrWhiteSpace(dataSrc))
                        {
                            if (dataSrc.StartsWith("/"))
                            {
                                var baseUrl = _config.BaseUrl.TrimEnd('/');
                                imageUrl = $"{baseUrl}{dataSrc}";
                            }
                            else
                            {
                                imageUrl = dataSrc;
                            }
                        }
                    }

                    entries.Add(new MikanAnimeEntry
                    {
                        MikanBangumiId = mikanBangumiId,
                        Title = HtmlEntity.DeEntitize(entryTitle),
                        ImageUrl = imageUrl,
                    });
                }

                if (entries.Count > 0)
                {
                    _logger.LogInformation("SearchAnimeEntries: found {Count} entries", entries.Count);
                    return entries;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SearchAnimeEntries failed for query mode={Mode}", query.Mode);
            }
        }

        return entries;
    }

    private static List<(string QueryValue, string Mode)> BuildSearchQueries(string title)
    {
        var queries = new List<(string QueryValue, string Mode)>();
        var dedupe = new HashSet<string>(StringComparer.Ordinal);

        AddQueryCandidate(queries, dedupe, EncodeSearchQueryForMikan(title), "form-encoded");

        var plusQuery = TryBuildPlusJoinedSearchQuery(title);
        AddQueryCandidate(queries, dedupe, plusQuery, "plus-joined");
        AddQueryCandidate(queries, dedupe, Uri.EscapeDataString(title), "escape-data-string");

        // Titles with slashes (e.g. "Fate/strange Fake") often get more results
        // when the slash is replaced with a space
        if (title.Contains('/'))
        {
            var slashStripped = title.Replace("/", " ").Replace("  ", " ").Trim();
            AddQueryCandidate(queries, dedupe, EncodeSearchQueryForMikan(slashStripped), "slash-stripped");
            var slashPlusQuery = TryBuildPlusJoinedSearchQuery(slashStripped);
            AddQueryCandidate(queries, dedupe, slashPlusQuery, "slash-stripped-plus");
        }

        return queries;
    }

    private static void AddQueryCandidate(
        ICollection<(string QueryValue, string Mode)> queries,
        ISet<string> dedupe,
        string? queryValue,
        string mode)
    {
        if (string.IsNullOrWhiteSpace(queryValue))
        {
            return;
        }

        if (!dedupe.Add(queryValue))
        {
            return;
        }

        queries.Add((queryValue, mode));
    }

    private static string EncodeSearchQueryForMikan(string title)
    {
        return WebUtility.UrlEncode(title);
    }

    private static string? TryBuildPlusJoinedSearchQuery(string title)
    {
        var tokens = title
            .Split([' ', '\t', '\r', '\n', '\u3000'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length < 2)
        {
            return null;
        }

        return string.Join("+", tokens.Select(EncodeSearchQueryForMikan));
    }

    private static bool TryResolveSearchQueryFromPseudoMikanId(string mikanBangumiId, out string query)
    {
        query = string.Empty;
        if (string.IsNullOrWhiteSpace(mikanBangumiId))
        {
            return false;
        }

        if (!mikanBangumiId.StartsWith(SearchPseudoMikanIdPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var raw = mikanBangumiId[SearchPseudoMikanIdPrefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        query = raw;
        return true;
    }

    private static string BuildSearchPseudoMikanId(string searchTerm)
    {
        return $"{SearchPseudoMikanIdPrefix}{searchTerm.Trim()}";
    }

    private MikanSearchResult? ParseSearchHtml(string htmlContent, string searchTerm)
    {
        _logger.LogInformation("Starting HTML parsing for search term: {SearchTerm}", searchTerm);

        var doc = new HtmlDocument();
        doc.LoadHtml(htmlContent);
        _logger.LogInformation("HTML loaded successfully");

        var bangumiLinks = doc.DocumentNode.SelectNodes("//a[contains(@href, '/Home/Bangumi/')]");
        if (bangumiLinks == null || bangumiLinks.Count == 0)
        {
            var fallback = TryParseEpisodeSearchFallback(doc, searchTerm);
            if (fallback != null)
            {
                return fallback;
            }

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
            var href = bangumiLink.GetAttributeValue("href", string.Empty);
            var bangumiId = ExtractBangumiIdFromBangumiUrl(href);
            if (string.IsNullOrEmpty(bangumiId) || !seenIds.Add(bangumiId))
            {
                continue;
            }

            var liElement = bangumiLink.Ancestors("li").FirstOrDefault();
            var titleSpan = liElement?.SelectSingleNode(".//span[contains(@class, 'an-title')]");

            var candidateTexts = new List<string>();
            if (!string.IsNullOrWhiteSpace(titleSpan?.InnerText))
            {
                candidateTexts.Add(HtmlEntity.DeEntitize(titleSpan.InnerText));
            }
            if (!string.IsNullOrWhiteSpace(bangumiLink.InnerText))
            {
                candidateTexts.Add(HtmlEntity.DeEntitize(bangumiLink.InnerText));
            }
            if (!string.IsNullOrWhiteSpace(liElement?.InnerText))
            {
                candidateTexts.Add(HtmlEntity.DeEntitize(liElement.InnerText));
            }

            var seasonNumber = 1;
            var year = 0;
            foreach (var text in candidateTexts)
            {
                var parsedSeason = TryExtractSeasonNumber(text);
                if (parsedSeason.HasValue && parsedSeason.Value > 0)
                {
                    seasonNumber = parsedSeason.Value;
                    break;
                }
            }

            foreach (var text in candidateTexts)
            {
                var parsedYear = TryExtractYear(text);
                if (parsedYear.HasValue)
                {
                    year = parsedYear.Value;
                    break;
                }
            }

            searchResult.Seasons.Add(new MikanSeasonInfo
            {
                SeasonName = $"Season {seasonNumber}",
                MikanBangumiId = bangumiId,
                Year = year,
                SeasonNumber = seasonNumber
            });
        }

        if (searchResult.Seasons.Count == 0)
        {
            _logger.LogWarning("No valid seasons found");
            return null;
        }

        searchResult.Seasons = searchResult.Seasons
            .OrderBy(s => s.SeasonNumber ?? int.MaxValue)
            .ThenBy(s => s.Year <= 0 ? int.MaxValue : s.Year)
            .ToList();

        searchResult.DefaultSeason = Math.Max(0, searchResult.Seasons.Count - 1);
        _logger.LogInformation("Found {Count} seasons via Bangumi links", searchResult.Seasons.Count);
        return searchResult;
    }

    private MikanSearchResult? TryParseEpisodeSearchFallback(HtmlDocument doc, string searchTerm)
    {
        var episodeLinks = doc.DocumentNode.SelectNodes("//a[contains(@href, '/Home/Episode/')]");
        if (episodeLinks == null || episodeLinks.Count == 0)
        {
            return null;
        }

        _logger.LogInformation(
            "Bangumi links not found; using RSS/Search fallback for term: {SearchTerm}, EpisodeLinks={Count}",
            searchTerm,
            episodeLinks.Count);

        return new MikanSearchResult
        {
            AnimeTitle = searchTerm,
            Seasons = new List<MikanSeasonInfo>
            {
                new()
                {
                    SeasonName = "Search Results",
                    MikanBangumiId = BuildSearchPseudoMikanId(searchTerm),
                    Year = 0,
                    SeasonNumber = 1
                }
            },
            DefaultSeason = 0
        };
    }

    private static int? TryExtractYear(string text)
    {
        var match = YearRegex().Match(text);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var year))
        {
            return null;
        }

        return year;
    }

    private static int? TryExtractSeasonNumber(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = SeasonNumberRegex().Match(text);
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

            if (int.TryParse(value, out var numeric) && numeric > 0)
            {
                return numeric;
            }

            var chinese = ParseChineseNumber(value);
            if (chinese > 0)
            {
                return chinese;
            }
        }

        return null;
    }

    private static int ParseChineseNumber(string value)
    {
        return value switch
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
    }

    private static string? NormalizeSubgroup(string? subgroup)
    {
        if (string.IsNullOrWhiteSpace(subgroup))
        {
            return null;
        }

        var trimmed = subgroup.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 48)
        {
            return null;
        }

        return NonSubgroupTokenRegex().IsMatch(trimmed) ? null : trimmed;
    }

    private string? ExtractBangumiIdFromBangumiUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return null;
        }

        var match = Regex.Match(url, @"/Home/Bangumi/(\d+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    private string? ExtractBangumiIdFromUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return null;
        }

        var match = Regex.Match(url, @"bangumiId=([^&]+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    public async Task<MikanFeedResponse> GetParsedFeedAsync(string mikanBangumiId)
    {
        _logger.LogInformation("Getting parsed feed for Mikan ID: {MikanId}", mikanBangumiId);
        var (cachedResponse, cachedUpdatedAt) = await TryLoadCachedFeedAsync(mikanBangumiId);
        if (cachedResponse != null &&
            cachedUpdatedAt.HasValue &&
            !IsFeedCacheExpired(cachedUpdatedAt.Value))
        {
            _logger.LogInformation(
                "Returning parsed feed from cache for Mikan ID: {MikanId} (updated at {UpdatedAt:O})",
                mikanBangumiId,
                cachedUpdatedAt.Value);
            // Attach persisted subgroup mapping from DB
            cachedResponse.SubgroupMapping = await LoadCachedSubgroupMappingAsync(mikanBangumiId);
            return cachedResponse;
        }

        try
        {
            // Parallel-fetch RSS feed and subgroup mapping from Mikan page
            var feedTask = GetAnimeFeedAsync(mikanBangumiId);
            var subgroupTask = GetSubgroupsAsync(mikanBangumiId);

            await Task.WhenAll(feedTask, subgroupTask);

            var feed = feedTask.Result;
            var subgroups = subgroupTask.Result;

            _logger.LogInformation("Retrieved RSS feed: Title={Title}, ItemsCount={Count}", feed.Title, feed.Items.Count);

            var response = BuildParsedFeedResponse(feed);
            response.SubgroupMapping = subgroups ?? new List<MikanSubgroupInfo>();

            // Persist feed cache and subgroup mapping sequentially —
            // both use the same scoped DbContext which is not thread-safe.
            await UpsertCachedFeedAsync(mikanBangumiId, response);
            await UpsertCachedSubgroupMappingAsync(mikanBangumiId, subgroups);

            return response;
        }
        catch (Exception ex) when (cachedResponse != null)
        {
            _logger.LogWarning(
                ex,
                "Failed to refresh parsed feed for Mikan ID: {MikanId}, using stale cache (updated at {UpdatedAt:O})",
                mikanBangumiId,
                cachedUpdatedAt);
            cachedResponse.SubgroupMapping = await LoadCachedSubgroupMappingAsync(mikanBangumiId);
            return cachedResponse;
        }
    }

    public async Task<List<MikanSubgroupInfo>?> GetSubgroupsAsync(string mikanBangumiId)
    {
        try
        {
            var url = $"Home/Bangumi/{mikanBangumiId}";
            _logger.LogInformation("Scraping Mikan Bangumi page for subgroups: {Url}", url);

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var htmlContent = await response.Content.ReadAsStringAsync();
            var doc = new HtmlDocument();
            doc.LoadHtml(htmlContent);

            var results = new List<MikanSubgroupInfo>();

            // Mikan page structure: each subgroup section has:
            //   <a href="/Home/PublishGroup/XXX">SubgroupName</a>
            //   <a href="/RSS/Bangumi?bangumiId=...&subgroupid=XXX" class="mikan-rss">...</a>
            var rssLinks = doc.DocumentNode.SelectNodes("//a[contains(@href, 'subgroupid=')]");
            if (rssLinks == null || rssLinks.Count == 0)
            {
                _logger.LogWarning("No subgroup RSS links found on Mikan page for {MikanBangumiId}", mikanBangumiId);
                return results; // empty list = page loaded fine, genuinely no subgroups
            }

            var seen = new HashSet<string>();
            foreach (var rssLink in rssLinks)
            {
                var href = rssLink.GetAttributeValue("href", "");
                var idMatch = Regex.Match(href, @"subgroupid=(\d+)");
                if (!idMatch.Success) continue;

                var subgroupId = idMatch.Groups[1].Value;
                if (seen.Contains(subgroupId)) continue;
                seen.Add(subgroupId);

                // The subgroup name link is the preceding sibling <a> with href containing /Home/PublishGroup/
                var parent = rssLink.ParentNode;
                if (parent == null) continue;

                var nameLink = parent.SelectSingleNode(".//a[contains(@href, '/Home/PublishGroup/')]");
                var name = nameLink != null
                    ? HtmlEntity.DeEntitize(nameLink.InnerText?.Trim() ?? "")
                    : "";

                if (string.IsNullOrWhiteSpace(name))
                {
                    name = $"Subgroup #{subgroupId}";
                }

                results.Add(new MikanSubgroupInfo
                {
                    SubgroupId = subgroupId,
                    Name = name
                });
            }

            _logger.LogInformation("Found {Count} subgroups for Mikan ID {MikanBangumiId}", results.Count, mikanBangumiId);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scrape subgroups for Mikan ID {MikanBangumiId}", mikanBangumiId);
            return null; // null = scrape failed, keep existing cache
        }
    }

    private MikanFeedResponse BuildParsedFeedResponse(MikanRssFeed feed)
    {
        var response = new MikanFeedResponse
        {
            SeasonName = feed.Title,
            Items = new List<ParsedRssItem>(),
            AvailableSubgroups = new List<string>(),
            AvailableResolutions = new List<string>(),
            AvailableSubtitleTypes = new List<string>(),
            EpisodeOffset = 0
        };

        var subgroupsSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolutionsSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var subtitleTypesSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in feed.Items.OrderByDescending(i => i.PublishedAt))
        {
            var parsed = _titleParser.ParseTitle(item.Title);
            var subgroup = NormalizeSubgroup(parsed.Subgroup);
            var normalizedHash = TorrentHashHelper.ResolveHash(
                item.TorrentHash,
                item.MagnetLink,
                item.TorrentUrl);
            var canDownload =
                !string.IsNullOrWhiteSpace(normalizedHash) &&
                (!string.IsNullOrWhiteSpace(item.MagnetLink) || !string.IsNullOrWhiteSpace(item.TorrentUrl));

            _logger.LogDebug(
                "Parsed item: Title={Title}, Resolution={Res}, Subgroup={Sub}, Episode={Ep}",
                item.Title,
                parsed.Resolution,
                subgroup,
                parsed.Episode);

            var parsedItem = new ParsedRssItem
            {
                Title = item.Title,
                TorrentUrl = item.TorrentUrl,
                MagnetLink = item.MagnetLink ?? string.Empty,
                TorrentHash = normalizedHash ?? string.Empty,
                CanDownload = canDownload,
                FileSize = item.FileSize ?? 0,
                FormattedSize = FormatFileSize(item.FileSize ?? 0),
                PublishedAt = item.PublishedAt,
                Resolution = parsed.Resolution,
                Subgroup = subgroup,
                SubtitleType = parsed.SubtitleType,
                Episode = parsed.Episode,
                IsCollection = parsed.IsCollection
            };

            response.Items.Add(parsedItem);

            if (!string.IsNullOrEmpty(subgroup))
            {
                subgroupsSet.Add(subgroup);
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

        response.AvailableSubgroups = subgroupsSet.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        response.AvailableResolutions = resolutionsSet.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        response.AvailableSubtitleTypes = subtitleTypesSet.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();

        var latestItem = response.Items.OrderByDescending(i => i.PublishedAt).FirstOrDefault();
        if (latestItem != null)
        {
            response.LatestPublishedAt = latestItem.PublishedAt;
            response.LatestTitle = latestItem.Title;
        }

        var episodeCandidates = response.Items
            .Where(i => i.Episode.HasValue)
            .Select(i => i.Episode!.Value)
            .ToList();
        if (episodeCandidates.Count > 0)
        {
            response.LatestEpisode = episodeCandidates.Max();
        }

        _logger.LogInformation(
            "Parsed feed '{Title}' with {Count} items, {Subgroups} subgroups, {Resolutions} resolutions, {SubtitleTypes} subtitle types",
            feed.Title,
            response.Items.Count,
            response.AvailableSubgroups.Count,
            response.AvailableResolutions.Count,
            response.AvailableSubtitleTypes.Count);

        return response;
    }

    private async Task<(MikanFeedResponse? response, DateTime? updatedAt)> TryLoadCachedFeedAsync(string mikanBangumiId)
    {
        if (_dbContext == null)
        {
            return (null, null);
        }

        var cache = await _dbContext.MikanFeedCaches
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.MikanBangumiId == mikanBangumiId);

        if (cache == null)
        {
            return (null, null);
        }

        var items = await _dbContext.MikanFeedItems
            .AsNoTracking()
            .Where(i => i.MikanBangumiId == mikanBangumiId)
            .OrderByDescending(i => i.PublishedAt)
            .ThenByDescending(i => i.Id)
            .ToListAsync();

        var response = new MikanFeedResponse
        {
            SeasonName = cache.SeasonName,
            EpisodeOffset = cache.EpisodeOffset,
            LatestEpisode = cache.LatestEpisode,
            LatestPublishedAt = cache.LatestPublishedAt,
            LatestTitle = cache.LatestTitle,
            Items = items.Select(MapEntityToParsedItem).ToList()
        };

        response.AvailableSubgroups = response.Items
            .Select(i => i.Subgroup)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .Select(s => s!)
            .ToList();

        response.AvailableResolutions = response.Items
            .Select(i => i.Resolution)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .Select(s => s!)
            .ToList();

        response.AvailableSubtitleTypes = response.Items
            .Select(i => i.SubtitleType)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .Select(s => s!)
            .ToList();

        if (!response.LatestPublishedAt.HasValue || string.IsNullOrWhiteSpace(response.LatestTitle))
        {
            var latestItem = response.Items.OrderByDescending(i => i.PublishedAt).FirstOrDefault();
            if (latestItem != null)
            {
                response.LatestPublishedAt = latestItem.PublishedAt;
                response.LatestTitle = latestItem.Title;
            }
        }

        if (!response.LatestEpisode.HasValue)
        {
            var latestEpisode = response.Items
                .Where(i => i.Episode.HasValue)
                .Select(i => i.Episode!.Value)
                .DefaultIfEmpty()
                .Max();
            response.LatestEpisode = latestEpisode > 0 ? latestEpisode : null;
        }

        return (response, cache.UpdatedAt);
    }

    private async Task UpsertCachedFeedAsync(string mikanBangumiId, MikanFeedResponse response)
    {
        if (_dbContext == null)
        {
            return;
        }

        var cache = await _dbContext.MikanFeedCaches
            .FirstOrDefaultAsync(c => c.MikanBangumiId == mikanBangumiId);

        if (cache == null)
        {
            cache = new MikanFeedCacheEntity
            {
                MikanBangumiId = mikanBangumiId
            };
            _dbContext.MikanFeedCaches.Add(cache);
        }

        cache.SeasonName = response.SeasonName;
        cache.LatestEpisode = response.LatestEpisode;
        cache.LatestPublishedAt = response.LatestPublishedAt;
        cache.LatestTitle = response.LatestTitle;
        cache.EpisodeOffset = response.EpisodeOffset;
        cache.UpdatedAt = DateTime.UtcNow;

        var existingItems = await _dbContext.MikanFeedItems
            .Where(i => i.MikanBangumiId == mikanBangumiId)
            .ToListAsync();
        if (existingItems.Count > 0)
        {
            _dbContext.MikanFeedItems.RemoveRange(existingItems);
        }

        foreach (var item in response.Items)
        {
            _dbContext.MikanFeedItems.Add(MapParsedItemToEntity(mikanBangumiId, item));
        }

        await _dbContext.SaveChangesAsync();
    }

    private async Task<List<MikanSubgroupInfo>> LoadCachedSubgroupMappingAsync(string mikanBangumiId)
    {
        if (_dbContext == null)
        {
            return new List<MikanSubgroupInfo>();
        }

        return await _dbContext.MikanSubgroups
            .AsNoTracking()
            .Where(s => s.MikanBangumiId == mikanBangumiId)
            .Select(s => new MikanSubgroupInfo
            {
                SubgroupId = s.SubgroupId,
                Name = s.SubgroupName
            })
            .ToListAsync();
    }

    /// <summary>
    /// Full-sync subgroup mapping for a given mikanBangumiId:
    /// upsert current rows and prune any that no longer exist on the Mikan page.
    /// Pass null to indicate scrape failure (keeps existing cache intact).
    /// </summary>
    private async Task UpsertCachedSubgroupMappingAsync(string mikanBangumiId, List<MikanSubgroupInfo>? subgroups)
    {
        if (_dbContext == null)
        {
            return;
        }

        // null means scrape failed — keep existing cache, don't prune
        if (subgroups == null)
        {
            _logger.LogDebug("Subgroup scrape returned null for Mikan ID {MikanBangumiId}, keeping existing cache", mikanBangumiId);
            return;
        }

        var now = DateTime.UtcNow;
        var currentIds = new HashSet<string>(subgroups.Select(sg => sg.SubgroupId));

        // Upsert current subgroups
        foreach (var sg in subgroups)
        {
            var existing = await _dbContext.MikanSubgroups
                .FirstOrDefaultAsync(s => s.MikanBangumiId == mikanBangumiId && s.SubgroupId == sg.SubgroupId);

            if (existing != null)
            {
                existing.SubgroupName = sg.Name;
                existing.UpdatedAt = now;
            }
            else
            {
                _dbContext.MikanSubgroups.Add(new MikanSubgroupEntity
                {
                    MikanBangumiId = mikanBangumiId,
                    SubgroupId = sg.SubgroupId,
                    SubgroupName = sg.Name,
                    UpdatedAt = now
                });
            }
        }

        // Prune stale entries no longer present on the Mikan page
        var staleEntries = await _dbContext.MikanSubgroups
            .Where(s => s.MikanBangumiId == mikanBangumiId && !currentIds.Contains(s.SubgroupId))
            .ToListAsync();

        if (staleEntries.Count > 0)
        {
            _dbContext.MikanSubgroups.RemoveRange(staleEntries);
            _logger.LogInformation("Pruned {Count} stale subgroup mappings for Mikan ID {MikanBangumiId}",
                staleEntries.Count, mikanBangumiId);
        }

        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("Synced {Count} subgroup mappings for Mikan ID {MikanBangumiId}", subgroups.Count, mikanBangumiId);
    }

    private bool IsFeedCacheExpired(DateTime updatedAt)
    {
        var utcUpdatedAt = updatedAt.Kind == DateTimeKind.Utc
            ? updatedAt
            : DateTime.SpecifyKind(updatedAt, DateTimeKind.Utc);

        return DateTime.UtcNow - utcUpdatedAt > _feedCacheTtl;
    }

    private static ParsedRssItem MapEntityToParsedItem(MikanFeedItemEntity entity)
    {
        return new ParsedRssItem
        {
            Title = entity.Title,
            TorrentUrl = entity.TorrentUrl,
            MagnetLink = entity.MagnetLink,
            TorrentHash = entity.TorrentHash,
            CanDownload = entity.CanDownload,
            FileSize = entity.FileSize,
            FormattedSize = entity.FormattedSize,
            PublishedAt = entity.PublishedAt,
            Resolution = entity.Resolution,
            Subgroup = entity.Subgroup,
            SubtitleType = entity.SubtitleType,
            Episode = entity.Episode,
            IsCollection = entity.IsCollection
        };
    }

    private static MikanFeedItemEntity MapParsedItemToEntity(string mikanBangumiId, ParsedRssItem item)
    {
        return new MikanFeedItemEntity
        {
            MikanBangumiId = mikanBangumiId,
            Title = item.Title,
            TorrentUrl = item.TorrentUrl,
            MagnetLink = item.MagnetLink,
            TorrentHash = item.TorrentHash,
            CanDownload = item.CanDownload,
            FileSize = item.FileSize,
            FormattedSize = item.FormattedSize,
            PublishedAt = item.PublishedAt,
            Resolution = item.Resolution,
            Subgroup = item.Subgroup,
            SubtitleType = item.SubtitleType,
            Episode = item.Episode,
            IsCollection = item.IsCollection
        };
    }

    private static string FormatFileSize(long bytes)
    {
        const long KB = 1024;
        const long MB = KB * 1024;
        const long GB = MB * 1024;

        if (bytes < KB)
        {
            return bytes + " B";
        }
        if (bytes < MB)
        {
            return ((double)bytes / KB).ToString("F1") + " KB";
        }
        if (bytes < GB)
        {
            return ((double)bytes / MB).ToString("F1") + " MB";
        }

        return ((double)bytes / GB).ToString("F2") + " GB";
    }
}
