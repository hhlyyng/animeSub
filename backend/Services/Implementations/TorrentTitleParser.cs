using System.Text.RegularExpressions;
using backend.Services.Interfaces;
using backend.Models.Dtos;

namespace backend.Services.Implementations;

/// <summary>
/// Extracts torrent title metadata such as subgroup, resolution, subtitle type, and episode.
/// </summary>
public partial class TorrentTitleParser : ITorrentTitleParser
{
    [GeneratedRegex(@"\b(1080p|720p|4k|2160p)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
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

    [GeneratedRegex(@"(\u7b80\u7e41\u5185\u5c01|\u7c21\u7e41\u5167\u5c01|\u7b80\u4f53\u5185\u5d4c|\u7e41\u4f53\u5185\u5d4c|\u7c21\u9ad4\u5167\u5d4c|\u7e41\u9ad4\u5167\u5d4c|CHT|CHS)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex SubtitleTypeRegex();

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

        var subtitleMatch = SubtitleTypeRegex().Match(title);
        if (subtitleMatch.Success)
        {
            info.SubtitleType = NormalizeSubtitleType(subtitleMatch.Value);
        }

        return info;
    }

    public string? NormalizeResolution(string? rawResolution)
    {
        if (string.IsNullOrWhiteSpace(rawResolution))
        {
            return null;
        }

        return rawResolution.Trim().ToLowerInvariant() switch
        {
            "2160p" or "4k" => "4K",
            "1080p" => "1080p",
            "720p" => "720p",
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
        // Some source titles only keep season marker (S2 / 第二季) without explicit episode.
        // We treat these as the season opener fallback.
        if (SeasonHintRegex().IsMatch(title))
        {
            return 1;
        }

        return null;
    }

    private static string NormalizeSubtitleType(string subtitle)
    {
        var normalized = subtitle.Trim().ToLowerInvariant();
        return normalized switch
        {
            "\u7b80\u7e41\u5185\u5c01" or "\u7c21\u7e41\u5167\u5c01" => "\u7b80\u7e41\u5185\u5c01",
            "\u7b80\u4f53\u5185\u5d4c" or "\u7c21\u9ad4\u5167\u5d4c" or "chs" => "\u7b80\u4f53\u5185\u5d4c",
            "\u7e41\u4f53\u5185\u5d4c" or "\u7e41\u9ad4\u5167\u5d4c" or "cht" => "\u7e41\u4f53\u5185\u5d4c",
            _ => subtitle.Trim()
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
