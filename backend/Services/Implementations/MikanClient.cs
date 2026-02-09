using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HtmlAgilityPack;
using Microsoft.Extensions.Options;
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
        ITorrentTitleParser titleParser)
    {
        _httpClient = httpClient;
        _logger = logger;
        _config = config.Value;
        _titleParser = titleParser;

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
                _logger.LogError(ex, "Failed to search Mikan HTML for title: {Title}, mode={Mode}", title, query.Mode);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing Mikan search result for: {Title}, mode={Mode}", title, query.Mode);
                return null;
            }
        }

        return null;
    }

    private static List<(string QueryValue, string Mode)> BuildSearchQueries(string title)
    {
        var queries = new List<(string QueryValue, string Mode)>();
        var dedupe = new HashSet<string>(StringComparer.Ordinal);

        AddQueryCandidate(queries, dedupe, EncodeSearchQueryForMikan(title), "form-encoded");

        var plusQuery = TryBuildPlusJoinedSearchQuery(title);
        AddQueryCandidate(queries, dedupe, plusQuery, "plus-joined");
        AddQueryCandidate(queries, dedupe, Uri.EscapeDataString(title), "escape-data-string");

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

        var feed = await GetAnimeFeedAsync(mikanBangumiId);
        _logger.LogInformation("Retrieved RSS feed: Title={Title}, ItemsCount={Count}", feed.Title, feed.Items.Count);

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
