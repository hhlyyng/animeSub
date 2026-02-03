namespace backend.Models.Configuration;

/// <summary>
/// Configuration for qBittorrent WebUI integration
/// </summary>
public class QBittorrentConfiguration
{
    public const string SectionName = "QBittorrent";

    /// <summary>
    /// qBittorrent WebUI host (default: localhost)
    /// </summary>
    public string Host { get; set; } = "localhost";

    /// <summary>
    /// qBittorrent WebUI port (default: 8080)
    /// </summary>
    public int Port { get; set; } = 8080;

    /// <summary>
    /// WebUI username (default: admin)
    /// </summary>
    public string Username { get; set; } = "admin";

    /// <summary>
    /// WebUI password
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Default save path for downloaded torrents
    /// If null, uses qBittorrent's default save path
    /// </summary>
    public string? DefaultSavePath { get; set; }

    /// <summary>
    /// Category to assign to anime downloads (default: anime)
    /// </summary>
    public string Category { get; set; } = "anime";

    /// <summary>
    /// Whether to pause torrents after adding (default: false)
    /// </summary>
    public bool PauseTorrentAfterAdd { get; set; } = false;

    /// <summary>
    /// HTTP request timeout in seconds (default: 30)
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Constructs the base URL for qBittorrent WebUI API
    /// </summary>
    public string GetBaseUrl() => $"http://{Host}:{Port}";
}
