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
    /// Comma-separated tags to assign to torrents added by AnimeSub (default: AnimeSub)
    /// qBittorrent Web API field name: "tags"
    /// </summary>
    public string Tags { get; set; } = "AnimeSub";

    /// <summary>
    /// Whether to pause torrents after adding (default: false)
    /// </summary>
    public bool PauseTorrentAfterAdd { get; set; } = false;

    /// <summary>
    /// HTTP request timeout in seconds (default: 30)
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Lockout window in seconds after reaching failed login threshold (default: 300)
    /// </summary>
    public int FailedLoginBlockSeconds { get; set; } = 300;

    /// <summary>
    /// Suspension window in seconds after qBittorrent becomes unreachable/timeout (default: 45)
    /// </summary>
    public int OfflineSuspendSeconds { get; set; } = 45;

    /// <summary>
    /// Constructs the base URL for qBittorrent WebUI API
    /// </summary>
    public string GetBaseUrl() => $"http://{Host}:{Port}";
}
