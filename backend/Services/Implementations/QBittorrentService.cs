using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using backend.Models.Configuration;
using backend.Services.Interfaces;

namespace backend.Services.Implementations;

/// <summary>
/// qBittorrent service implementation for torrent management
/// Handles authentication and API calls to qBittorrent WebUI
/// </summary>
public class QBittorrentService : IQBittorrentService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<QBittorrentService> _logger;
    private readonly QBittorrentConfiguration _config;

    private string _sessionCookie = string.Empty;
    private DateTime _sessionExpiry = DateTime.MinValue;
    private readonly SemaphoreSlim _loginLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public QBittorrentService(
        HttpClient httpClient,
        ILogger<QBittorrentService> logger,
        IOptions<QBittorrentConfiguration> config)
    {
        _httpClient = httpClient;
        _logger = logger;
        _config = config.Value;

        _httpClient.Timeout = TimeSpan.FromSeconds(_config.TimeoutSeconds);
    }

    private string BaseUrl => _config.GetBaseUrl();

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            await EnsureAuthenticatedAsync();
            // Try to get app version as a simple connectivity test
            var request = CreateRequest(HttpMethod.Get, "/api/v2/app/version");
            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to qBittorrent at {BaseUrl}", BaseUrl);
            return false;
        }
    }

    public async Task<bool> AddTorrentAsync(string torrentUrl, string? savePath = null, string? category = null, bool? paused = null)
    {
        await EnsureAuthenticatedAsync();

        var formData = new List<KeyValuePair<string, string>>
        {
            new("urls", torrentUrl)
        };

        // Apply defaults from config, then override with parameters
        var actualSavePath = savePath ?? _config.DefaultSavePath;
        var actualCategory = category ?? _config.Category;
        var actualPaused = paused ?? _config.PauseTorrentAfterAdd;

        if (!string.IsNullOrEmpty(actualSavePath))
        {
            formData.Add(new("savepath", actualSavePath));
        }

        if (!string.IsNullOrEmpty(actualCategory))
        {
            formData.Add(new("category", actualCategory));
        }

        if (actualPaused)
        {
            formData.Add(new("paused", "true"));
        }

        var request = CreateRequest(HttpMethod.Post, "/api/v2/torrents/add");
        request.Content = new FormUrlEncodedContent(formData);

        try
        {
            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Added torrent: {Url}", torrentUrl);
                return true;
            }

            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Failed to add torrent. Status: {Status}, Response: {Response}",
                response.StatusCode, errorContent);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding torrent: {Url}", torrentUrl);
            return false;
        }
    }

    public async Task<List<QBTorrentInfo>> GetTorrentsAsync(string? category = null)
    {
        await EnsureAuthenticatedAsync();

        var url = "/api/v2/torrents/info";
        if (!string.IsNullOrEmpty(category))
        {
            url += $"?category={Uri.EscapeDataString(category)}";
        }

        var request = CreateRequest(HttpMethod.Get, url);

        try
        {
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get torrents list. Status: {Status}", response.StatusCode);
                return new List<QBTorrentInfo>();
            }

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<QBTorrentInfo>>(json, JsonOptions) ?? new List<QBTorrentInfo>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting torrents list");
            return new List<QBTorrentInfo>();
        }
    }

    public async Task<QBTorrentInfo?> GetTorrentAsync(string hash)
    {
        var torrents = await GetTorrentsAsync();
        return torrents.FirstOrDefault(t =>
            t.Hash.Equals(hash, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> TorrentExistsAsync(string hash)
    {
        var torrent = await GetTorrentAsync(hash);
        return torrent != null;
    }

    public async Task PauseTorrentAsync(string hash)
    {
        await EnsureAuthenticatedAsync();

        var request = CreateRequest(HttpMethod.Post, "/api/v2/torrents/pause");
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("hashes", hash)
        });

        try
        {
            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Paused torrent: {Hash}", hash);
            }
            else
            {
                _logger.LogWarning("Failed to pause torrent {Hash}. Status: {Status}", hash, response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pausing torrent: {Hash}", hash);
        }
    }

    public async Task ResumeTorrentAsync(string hash)
    {
        await EnsureAuthenticatedAsync();

        var request = CreateRequest(HttpMethod.Post, "/api/v2/torrents/resume");
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("hashes", hash)
        });

        try
        {
            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Resumed torrent: {Hash}", hash);
            }
            else
            {
                _logger.LogWarning("Failed to resume torrent {Hash}. Status: {Status}", hash, response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resuming torrent: {Hash}", hash);
        }
    }

    public async Task DeleteTorrentAsync(string hash, bool deleteFiles = false)
    {
        await EnsureAuthenticatedAsync();

        var request = CreateRequest(HttpMethod.Post, "/api/v2/torrents/delete");
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("hashes", hash),
            new KeyValuePair<string, string>("deleteFiles", deleteFiles.ToString().ToLower())
        });

        try
        {
            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Deleted torrent: {Hash} (deleteFiles: {DeleteFiles})", hash, deleteFiles);
            }
            else
            {
                _logger.LogWarning("Failed to delete torrent {Hash}. Status: {Status}", hash, response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting torrent: {Hash}", hash);
        }
    }

    #region Authentication

    private async Task EnsureAuthenticatedAsync()
    {
        if (!string.IsNullOrEmpty(_sessionCookie) && DateTime.UtcNow < _sessionExpiry)
        {
            return;
        }

        await _loginLock.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            if (!string.IsNullOrEmpty(_sessionCookie) && DateTime.UtcNow < _sessionExpiry)
            {
                return;
            }

            await LoginAsync();
        }
        finally
        {
            _loginLock.Release();
        }
    }

    private async Task LoginAsync()
    {
        var loginUrl = $"{BaseUrl}/api/v2/auth/login";

        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("username", _config.Username),
            new KeyValuePair<string, string>("password", _config.Password)
        });

        try
        {
            var response = await _httpClient.PostAsync(loginUrl, formData);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("qBittorrent login failed. Status: {Status}", response.StatusCode);
                throw new InvalidOperationException($"qBittorrent login failed with status {response.StatusCode}");
            }

            _sessionCookie = ExtractSessionCookie(response) ?? string.Empty;

            if (string.IsNullOrEmpty(_sessionCookie))
            {
                _logger.LogError("qBittorrent login succeeded but no session cookie received");
                throw new InvalidOperationException("qBittorrent login succeeded but no session cookie received");
            }

            // Session valid for 1 hour, refresh after 50 minutes
            _sessionExpiry = DateTime.UtcNow.AddMinutes(50);
            _logger.LogInformation("Successfully authenticated with qBittorrent at {BaseUrl}", BaseUrl);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to connect to qBittorrent at {BaseUrl}", BaseUrl);
            throw;
        }
    }

    private static string? ExtractSessionCookie(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            foreach (var cookie in cookies)
            {
                if (cookie.StartsWith("SID=", StringComparison.OrdinalIgnoreCase))
                {
                    return cookie.Split(';')[0];
                }
            }
        }
        return null;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string endpoint)
    {
        var url = $"{BaseUrl}{endpoint}";
        var request = new HttpRequestMessage(method, url);

        if (!string.IsNullOrEmpty(_sessionCookie))
        {
            request.Headers.Add("Cookie", _sessionCookie);
        }

        return request;
    }

    #endregion
}
