using System.Text.RegularExpressions;
using backend.Models.Dtos;
using backend.Services.Interfaces;

namespace backend.Services.Implementations;

/// <summary>
/// Extracts torrent title metadata such as subgroup, resolution, subtitle type, and episode.
/// </summary>
public partial class TorrentTitleParser : ITorrentTitleParser
{
    [GeneratedRegex(@"\b(ai2160p|ai4k|1080p|720p|4k|2160p|3840\s*(?:x|\u00d7|\*)\s*2160|1920\s*(?:x|\u00d7|\*)\s*1080|1280\s*(?:x|\u00d7|\*)\s*720)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex ResolutionRegex();

    [GeneratedRegex(@"^\s*(?:\[([^\]]+)\]|\u3010([^\u3011]+)\u3011)", RegexOptions.Compiled)]
    private static partial Regex SubgroupRegex();

    [GeneratedRegex(@"\u7b2c\s*0*(\d{1,3})\s*(?:\u8bdd|\u8a71|\u96c6)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeZhRegex();

    [GeneratedRegex(@"s\d{1,2}\s*e\s*0*(\d{1,3})", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeSeasonEpisodeRegex();

    [GeneratedRegex(@"(?:^|[\s._\-\[])(?:ep?|e)\s*0*(\d{1,3})(?:v\d+)?(?:$|[\s\]_.\-\[])", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex EpisodePrefixedRegex();

    [GeneratedRegex(@"\[\s*0*(\d{1,3})(?:v\d+)?\s*\]", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeBracketRegex();

    [GeneratedRegex(@"(?:^|[\s._\-])0*(\d{1,3})(?:v\d+)?(?:$|[\s\]_.\-])", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeLooseRegex();

    [GeneratedRegex(@"(?:\bS\d{1,2}\b|\bSeason\s*\d{1,2}\b|\b\d{1,2}(?:st|nd|rd|th)\s*Season\b|\u7b2c\s*[0-9\u4e00\u4e8c\u4e09\u56db\u4e94\u516d\u4e03\u516b\u4e5d\u5341]+\s*\u5b63)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex SeasonHintRegex();

    [GeneratedRegex(@"(?:\u5408\u96c6|\u5168\u96c6|\u96c6\u5408|batch|complete\s*(?:season|series|collection)?|collection)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex CollectionRegex();

    [GeneratedRegex(@"(简繁|繁简|簡繁|繁簡|简日|簡日|繁日|简体|繁体|簡體|繁體|CHS|CHT)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex SubtitleLanguageRegex();

    [GeneratedRegex(@"(内封|內封|内嵌|內嵌|外挂|外掛)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex SubtitleStyleRegex();

    public ParsedTorrentInfo ParseTitle(string title)
    {
        var info = new ParsedTorrentInfo();
        if (string.IsNullOrWhiteSpace(title))
        {
            return info;
        }

        var resolutionMatch = ResolutionRegex().Match(title);
        if (resolutionMatch.Success)
        {
            info.Resolution = NormalizeResolution(resolutionMatch.Value);
        }

        info.Subgroup = ExtractSubgroup(title);
        info.IsCollection = IsCollectionTitle(title);

        var episode = info.IsCollection ? null : ExtractEpisode(title);
        if (!episode.HasValue && !info.IsCollection)
        {
            episode = InferEpisodeFromSeasonHint(title);
        }

        if (episode.HasValue)
        {
            info.Episode = episode.Value;
        }

        var subtitleType = ExtractSubtitleType(title);
        if (!string.IsNullOrWhiteSpace(subtitleType))
        {
            info.SubtitleType = subtitleType;
        }

        return info;
    }

    public string? NormalizeResolution(string? rawResolution)
    {
        if (string.IsNullOrWhiteSpace(rawResolution))
        {
            return null;
        }

        var normalized = rawResolution
            .Trim()
            .ToLowerInvariant()
            .Replace("\u00d7", "x", StringComparison.Ordinal)
            .Replace("*", "x", StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        return normalized switch
        {
            "ai2160p" or "ai4k" => "AI4K",
            "2160p" or "4k" or "3840x2160" => "4K",
            "1080p" or "1920x1080" => "1080p",
            "720p" or "1280x720" => "720p",
            _ => null
        };
    }

    private static int? ExtractEpisode(string title)
    {
        var regexes = new[]
        {
            EpisodeZhRegex(),
            EpisodeSeasonEpisodeRegex(),
            EpisodePrefixedRegex(),
            EpisodeBracketRegex(),
            EpisodeLooseRegex()
        };

        foreach (var regex in regexes)
        {
            foreach (Match match in regex.Matches(title))
            {
                if (!match.Success || match.Groups.Count < 2)
                {
                    continue;
                }

                if (!int.TryParse(match.Groups[1].Value, out var episode))
                {
                    continue;
                }

                if (IsLikelyEpisode(title, episode))
                {
                    return episode;
                }
            }
        }

        return null;
    }

    private static bool IsLikelyEpisode(string title, int episode)
    {
        if (episode <= 0 || episode > 500)
        {
            return false;
        }

        // Common resolution values that should never be treated as episode numbers.
        if (episode is 360 or 480 or 540 or 720 or 1080 or 1440 or 2160)
        {
            return false;
        }

        if (Regex.IsMatch(title, $@"\b{episode}p\b", RegexOptions.IgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool IsCollectionTitle(string title)
    {
        return CollectionRegex().IsMatch(title);
    }

    private static int? InferEpisodeFromSeasonHint(string title)
    {
        // Some source titles only keep season markers (S2 / 第二季) without explicit episode.
        // Treat these as season openers.
        if (SeasonHintRegex().IsMatch(title))
        {
            return 1;
        }

        return null;
    }

    private static string? ExtractSubtitleType(string title)
    {
        var language = ExtractSubtitleLanguage(title);
        var style = ExtractSubtitleStyle(title);

        if (language is null && style is null)
        {
            return null;
        }

        if (language is null)
        {
            return style;
        }

        if (style is null)
        {
            return language;
        }

        return $"{language}{style}";
    }

    private static string? ExtractSubtitleLanguage(string title)
    {
        var match = SubtitleLanguageRegex().Match(title);
        if (!match.Success)
        {
            return null;
        }

        var normalized = match.Value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "简繁" or "繁简" or "簡繁" or "繁簡" => "简繁",
            "简体" or "簡體" or "chs" => "简体",
            "繁体" or "繁體" or "cht" => "繁体",
            "简日" or "簡日" => "简日",
            "繁日" => "繁日",
            _ => null
        };
    }

    private static string? ExtractSubtitleStyle(string title)
    {
        var match = SubtitleStyleRegex().Match(title);
        if (!match.Success)
        {
            return null;
        }

        var normalized = match.Value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "内封" or "內封" => "内封",
            "内嵌" or "內嵌" => "内嵌",
            "外挂" or "外掛" => "外挂",
            _ => null
        };
    }

    private static string? ExtractSubgroup(string title)
    {
        var subgroupMatch = SubgroupRegex().Match(title);
        if (!subgroupMatch.Success)
        {
            return null;
        }

        for (var i = 1; i < subgroupMatch.Groups.Count; i++)
        {
            var value = subgroupMatch.Groups[i].Value?.Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
