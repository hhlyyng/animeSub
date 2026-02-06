using System.Text.RegularExpressions;
using backend.Services.Interfaces;
using backend.Models.Dtos;

namespace backend.Services.Implementations;

/// <summary>
/// Implementation for parsing torrent title metadata
/// Extracts resolution, subgroup, subtitle type, and episode from RSS titles
/// </summary>
public partial class TorrentTitleParser : ITorrentTitleParser
{
    // Regex patterns for extracting metadata
    [GeneratedRegex(@"(1080[pP]|720[pP]|4[kK]|2160[pP])", RegexOptions.Compiled)]
    private static partial Regex ResolutionRegex();

    [GeneratedRegex(@"^\[([^\]]+)\]", RegexOptions.Compiled)]
    private static partial Regex SubgroupRegex();

    [GeneratedRegex(@"(?:第|EP?)(\d+)|[\s_-](\d{2,3})[\s_\[\.]", RegexOptions.Compiled)]
    private static partial Regex EpisodeRegex();

    [GeneratedRegex(@"(简日|繁日|简体|繁体|内嵌|CHT|CHS)", RegexOptions.Compiled)]
    private static partial Regex SubtitleTypeRegex();

    public ParsedTorrentInfo ParseTitle(string title)
    {
        var info = new ParsedTorrentInfo();

        if (string.IsNullOrWhiteSpace(title))
        {
            return info;
        }

        // Extract resolution
        var resolutionMatch = ResolutionRegex().Match(title);
        if (resolutionMatch.Success)
        {
            info.Resolution = NormalizeResolution(resolutionMatch.Value);
        }

        // Extract subgroup (first bracket)
        var subgroupMatch = SubgroupRegex().Match(title);
        if (subgroupMatch.Success)
        {
            info.Subgroup = subgroupMatch.Groups[1].Value;
        }

        // Extract episode number
        var episodeMatch = EpisodeRegex().Match(title);
        if (episodeMatch.Success)
        {
            var episodeStr = episodeMatch.Groups[1].Success 
                ? episodeMatch.Groups[1].Value 
                : episodeMatch.Groups[2].Value;
            
            if (int.TryParse(episodeStr, out var episode))
            {
                info.Episode = episode;
            }
        }

        // Extract subtitle type
        var subtitleMatch = SubtitleTypeRegex().Match(title);
        if (subtitleMatch.Success)
        {
            var subtitleStr = subtitleMatch.Value;
            info.SubtitleType = NormalizeSubtitleType(subtitleStr);
        }

        return info;
    }

    public string? NormalizeResolution(string? rawResolution)
    {
        if (string.IsNullOrWhiteSpace(rawResolution))
        {
            return null;
        }

        return rawResolution.ToLowerInvariant() switch
        {
            "2160p" or "4k" => "4K",
            "1080p" => "1080p",
            "720p" => "720p",
            _ => null // Other formats treated as null, frontend will show "其他"
        };
    }

    private string? NormalizeSubtitleType(string subtitle)
    {
        return subtitle switch
        {
            "简日" or "内嵌" => "简日内嵌",
            "繁日" => "繁日",
            "简体" or "CHS" => "简体",
            "繁体" or "CHT" => "繁体",
            _ => subtitle
        };
    }
}