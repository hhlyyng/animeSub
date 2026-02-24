using backend.Data.Entities;

namespace backend.Services.Interfaces;

/// <summary>
/// qBittorrent service for torrent management
/// </summary>
public interface IQBittorrentService
{
    /// <summary>
    /// Test connection to qBittorrent WebUI
    /// </summary>
    /// <returns>True if connection successful</returns>
    Task<bool> TestConnectionAsync();

    /// <summary>
    /// Add a torrent by URL (torrent file URL or magnet link)
    /// </summary>
    /// <param name="torrentUrl">URL to torrent file or magnet link</param>
    /// <param name="savePath">Optional custom save path</param>
    /// <param name="category">Optional category override</param>
    /// <param name="paused">Whether to add in paused state</param>
    /// <returns>True if torrent was added successfully</returns>
    Task<bool> AddTorrentAsync(string torrentUrl, string? savePath = null, string? category = null, bool? paused = null);

    /// <summary>
    /// Get list of all torrents
    /// </summary>
    /// <param name="category">Optional filter by category</param>
    /// <returns>List of torrent information</returns>
    Task<List<QBTorrentInfo>> GetTorrentsAsync(string? category = null);

    /// <summary>
    /// Get torrent by hash
    /// </summary>
    /// <param name="hash">Torrent info hash</param>
    /// <returns>Torrent info or null if not found</returns>
    Task<QBTorrentInfo?> GetTorrentAsync(string hash);

    /// <summary>
    /// Check if torrent exists by hash
    /// </summary>
    /// <param name="hash">Torrent info hash</param>
    /// <returns>True if torrent exists</returns>
    Task<bool> TorrentExistsAsync(string hash);

    /// <summary>
    /// Pause a torrent
    /// </summary>
    /// <param name="hash">Torrent info hash</param>
    Task PauseTorrentAsync(string hash);

    /// <summary>
    /// Resume a torrent
    /// </summary>
    /// <param name="hash">Torrent info hash</param>
    Task ResumeTorrentAsync(string hash);

    /// <summary>
    /// Delete a torrent
    /// </summary>
    /// <param name="hash">Torrent info hash</param>
    /// <param name="deleteFiles">Whether to delete downloaded files</param>
    /// <returns>True if deletion was successful</returns>
    Task<bool> DeleteTorrentAsync(string hash, bool deleteFiles = false);

    /// <summary>
    /// Add torrent with database tracking
    /// </summary>
    /// <param name="torrentUrl">URL to torrent file or magnet link</param>
    /// <param name="torrentHash">Torrent magnet hash</param>
    /// <param name="title">Torrent title</param>
    /// <param name="fileSize">File size in bytes</param>
    /// <param name="source">Download source (Manual/Subscription)</param>
    /// <param name="subscriptionId">Optional subscription ID</param>
    /// <param name="animeTitle">Optional anime title used to build the sub-path when UseAnimeSubPath is enabled</param>
    /// <returns>True if torrent was added successfully</returns>
    Task<bool> AddTorrentWithTrackingAsync(
        string torrentUrl,
        string torrentHash,
        string title,
        long fileSize,
        DownloadSource source,
        int? subscriptionId = null,
        string? animeTitle = null);

    /// <summary>
    /// Sync download progress for manual downloads from qBittorrent
    /// </summary>
    /// <returns>Number of torrents synced</returns>
    Task<int> SyncManualDownloadsProgressAsync();
}

/// <summary>
/// Torrent information from qBittorrent API
/// </summary>
public class QBTorrentInfo
{
    public string Hash { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long Size { get; set; }
    public double Progress { get; set; }
    public string State { get; set; } = string.Empty;
    public long Dlspeed { get; set; }
    public long Upspeed { get; set; }
    public int NumSeeds { get; set; }
    public int NumLeechs { get; set; }
    public string? Category { get; set; }
    public string? SavePath { get; set; }
    public int? Eta { get; set; }
    public long AddedOn { get; set; }
    public long CompletionOn { get; set; }
}
