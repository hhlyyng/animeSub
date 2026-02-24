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

    // Bug 6: allow up to 20 non-bracket chars before the opening bracket (搬运/转载 prefixes)
    [GeneratedRegex(@"^[^\[【]{0,20}(?:\[([^\]]+)\]|\u3010([^\u3011]+)\u3011)", RegexOptions.Compiled)]
    private static partial Regex SubgroupRegex();

    // Bug 1: \d{1,3} → \d{1,4} to support up to 9999 episodes
    [GeneratedRegex(@"\u7b2c\s*0*(\d{1,4})\s*(?:\u8bdd|\u8a71|\u96c6)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeZhRegex();

    // Bug 1 (new): handles 第07&08话 — takes the last number in an &-range
    [GeneratedRegex(@"\u7b2c\s*\d{1,4}\s*[&\uff06]\s*0*(\d{1,4})\s*(?:\u8bdd|\u8a71|\u96c6)", RegexOptions.Compiled)]
    private static partial Regex EpisodeZhRangeRegex();

    [GeneratedRegex(@"s\d{1,2}\s*e\s*0*(\d{1,4})", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeSeasonEpisodeRegex();

    [GeneratedRegex(@"(?:^|[\s._\-\[])(?:ep?|e)\s*0*(\d{1,4})(?:v\d+)?(?:$|[\s\]_.\-\[])", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex EpisodePrefixedRegex();

    [GeneratedRegex(@"\[\s*0*(\d{1,4})(?:v\d+)?\s*\]", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeBracketRegex();

    [GeneratedRegex(@"(?:^|[\s._\-])0*(\d{1,4})(?:v\d+)?(?:$|[\s\]_.\-])", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeLooseRegex();

    [GeneratedRegex(@"(?:\bS\d{1,2}\b|\bSeason\s*\d{1,2}\b|\b\d{1,2}(?:st|nd|rd|th)\s*Season\b|\u7b2c\s*[0-9\u4e00\u4e8c\u4e09\u56db\u4e94\u516d\u4e03\u516b\u4e5d\u5341]+\s*\u5b63)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex SeasonHintRegex();

    [GeneratedRegex(@"(?:\u5408\u96c6|\u5168\u96c6|\u96c6\u5408|batch|complete\s*(?:season|series|collection)?|collection)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex CollectionRegex();

    // Bug 5 (new): range episodes like 01-12, 841-847; also Part 1+2 style batch releases
    // Bug 7: negative lookbehind (?<![A-Za-z0-9]) prevents matching when the first digit is
    //   part of an ASCII word token, e.g. S02 - 23 (at '2', preceded by '0') or S2 - 23
    //   (at '2', preceded by 'S'). Using \w was too broad — .NET \w matches Unicode letters
    //   including CJK, which would block legitimate ranges like 第841-847话.
    [GeneratedRegex(@"(?<![A-Za-z0-9])\d{1,4}\s*[-~\uff5e+\uff0b]\s*\d{1,4}", RegexOptions.Compiled)]
    private static partial Regex CollectionRangeRegex();

    // Bug 4: added 简繁日 / 簡繁日 / 简/繁 / 繁/简 patterns
    [GeneratedRegex(
        @"(简繁日|簡繁日|简繁|繁简|簡繁|繁簡|简日|簡日|繁日|简体|繁体|簡體|繁體|简/繁|繁/简|CHS|CHT)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex SubtitleLanguageRegex();

    [GeneratedRegex(@"(内封|內封|内嵌|內嵌|外挂|外掛)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex SubtitleStyleRegex();

    private static readonly string[] ResolutionPriority = ["AI4K", "4K", "1080p", "720p"];

    public ParsedTorrentInfo ParseTitle(string title)
    {
        var info = new ParsedTorrentInfo();
        if (string.IsNullOrWhiteSpace(title))
        {
            return info;
        }

        // Bug 3: collect all resolution matches, pick highest
        var allResolutions = ResolutionRegex()
            .Matches(title)
            .Select(m => NormalizeResolution(m.Value))
            .OfType<string>();
        info.Resolution = PickHighestResolution(allResolutions);

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

    private static string? PickHighestResolution(IEnumerable<string> normalized)
    {
        string? best = null;
        int bestRank = int.MaxValue;
        foreach (var r in normalized)
        {
            var rank = Array.IndexOf(ResolutionPriority, r);
            if (rank >= 0 && rank < bestRank)
            {
                best = r;
                bestRank = rank;
            }
        }
        return best;
    }

    // Bug 2: candidate scoring instead of first-match
    private record struct EpisodeCandidate(int Value, int Score, int Position);

    private static int? ExtractEpisode(string title)
    {
        var highCandidates = new List<EpisodeCandidate>();

        void Collect(Regex regex, int score)
        {
            foreach (Match m in regex.Matches(title))
            {
                if (m.Groups.Count >= 2 && int.TryParse(m.Groups[1].Value, out var ep)
                    && IsLikelyEpisode(title, ep))
                    highCandidates.Add(new(ep, score, m.Index));
            }
        }

        Collect(EpisodeZhRangeRegex(), 95); // 第07&08话 → last number
        Collect(EpisodeZhRegex(), 100);     // 第X话/集
        Collect(EpisodeSeasonEpisodeRegex(), 90); // S01E05
        Collect(EpisodePrefixedRegex(), 80);      // EP/E prefix
        Collect(EpisodeBracketRegex(), 70);       // [13]

        if (highCandidates.Count > 0)
            return highCandidates
                .OrderByDescending(c => c.Score)
                .ThenBy(c => c.Position)
                .First().Value;

        // Loose mode: dynamic scoring
        var looseCandidates = new List<EpisodeCandidate>();
        foreach (Match m in EpisodeLooseRegex().Matches(title))
        {
            if (m.Groups.Count < 2) continue;
            if (!int.TryParse(m.Groups[1].Value, out var ep)) continue;
            if (!IsLikelyEpisode(title, ep)) continue;

            int score = 30;

            // Bonus: leading zero (01, 02 — episode convention)
            if (m.Groups[1].Value.StartsWith('0')) score += 30;

            // Bonus: preceded by separator dash
            var preceding = title[..m.Index].TrimEnd();
            if (preceding.EndsWith('-') || preceding.EndsWith('\u2014')) score += 15;

            // Penalty: digit followed by CJK quantifier → part of title noun
            var afterIdx = m.Index + m.Length;
            if (afterIdx < title.Length)
            {
                var ch = title.AsSpan(afterIdx).TrimStart();
                if (!ch.IsEmpty && IsQuantifierChar(ch[0])) score -= 50;
            }

            // Penalty: this number matches the season marker
            if (Regex.IsMatch(title, $@"\bS{ep}\b", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(title, $@"\bSeason\s*{ep}\b", RegexOptions.IgnoreCase))
                score -= 40;

            looseCandidates.Add(new(ep, score, m.Index));
        }

        // Among positive-score loose candidates, prefer higher score then later position
        return looseCandidates
            .Where(c => c.Score > 0)
            .OrderByDescending(c => c.Score)
            .ThenByDescending(c => c.Position)
            .Select(c => (int?)c.Value)
            .FirstOrDefault();
    }

    private static bool IsQuantifierChar(char c) =>
        c is '个' or '位' or '名' or '号' or '年' or '番' or '局' or '场';

    // Bug 1: raised upper limit from 500 to 20000
    private static bool IsLikelyEpisode(string title, int episode)
    {
        if (episode <= 0 || episode > 20000)
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

        // Video codec identifiers: x264, h264, x265, x.264, h.265, etc.
        if (Regex.IsMatch(title, $@"\b[xh][.\-_]?{episode}\b", RegexOptions.IgnoreCase))
        {
            return false;
        }

        return true;
    }

    // Bug 5: range detection + single-episode guard
    private static bool IsCollectionTitle(string title)
    {
        // Double-episode marker 第07&08话 → specific episode pair, not a batch collection
        if (EpisodeZhRangeRegex().IsMatch(title)) return false;

        // Explicit single-episode marker (第X话/集) without a dash range → not a collection
        bool hasSingleEpisodeMarker =
            Regex.IsMatch(title, @"\u7b2c\s*\d{1,4}\s*(?:\u8bdd|\u8a71|\u96c6)") &&
            !CollectionRangeRegex().IsMatch(title);

        if (hasSingleEpisodeMarker) return false;

        // Dash range like 01-12 or 841-847 → collection
        if (CollectionRangeRegex().IsMatch(title)) return true;

        // Keywords: 合集, 全集, batch, collection …
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

    // Bug 4: aggregate multiple language tokens
    private static string? ExtractSubtitleLanguage(string title)
    {
        var tokens = SubtitleLanguageRegex()
            .Matches(title)
            .Select(m => NormalizeLangToken(m.Value))
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (tokens.Count == 0) return null;

        bool hasChs = tokens.Contains("简体");
        bool hasCht = tokens.Contains("繁体");
        bool hasJp = tokens.Contains("日");

        if (tokens.Contains("简繁日")) return "简繁日";
        if (tokens.Contains("简繁")) return "简繁";
        if ((hasChs && hasCht) || tokens.Contains("简/繁")) return "简繁";
        if (tokens.Contains("简日")) return "简日";
        if (tokens.Contains("繁日")) return "繁日";
        if (hasChs && hasJp) return "简日";
        if (hasCht && hasJp) return "繁日";
        if (hasChs) return "简体";
        if (hasCht) return "繁体";

        return tokens.FirstOrDefault();
    }

    private static string? NormalizeLangToken(string raw)
    {
        return raw.Trim().ToLowerInvariant() switch
        {
            "简繁日" or "簡繁日" => "简繁日",
            "简繁" or "繁简" or "簡繁" or "繁簡" or "简/繁" or "繁/简" => "简繁",
            "简日" or "簡日" => "简日",
            "繁日" => "繁日",
            "简体" or "簡體" or "chs" => "简体",
            "繁体" or "繁體" or "cht" => "繁体",
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
