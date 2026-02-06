using backend.Models.Dtos;

namespace backend.Services.Interfaces;

/// <summary>
/// Interface for parsing torrent title metadata
/// Extracts resolution, subgroup, subtitle type, and episode from RSS titles
/// </summary>
public interface ITorrentTitleParser
{
    /// <summary>
    /// Parse torrent title to extract metadata
    /// </summary>
    Models.Dtos.ParsedTorrentInfo ParseTitle(string title);

    /// <summary>
    /// Normalize resolution string to standard format
    /// </summary>
    string? NormalizeResolution(string? rawResolution);
}