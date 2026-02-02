using backend.Services.Interfaces;

namespace backend.Services.Implementations
{

/// <summary>
/// qBittorrent service implementation for torrent management
/// </summary>
public class QBittorrentService : IQBittorrentService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<QBittorrentService> _logger;

    public QBittorrentService(HttpClient httpClient, ILogger<QBittorrentService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
}
}
