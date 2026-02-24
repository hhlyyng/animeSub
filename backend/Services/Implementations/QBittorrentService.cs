using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using backend.Models.Configuration;
using backend.Services.Exceptions;
using backend.Services.Interfaces;
using backend.Data.Entities;

namespace backend.Services.Implementations;

/// <summary>
/// qBittorrent service implementation for torrent management
/// Handles authentication and API calls to qBittorrent WebUI
/// </summary>
public class QBittorrentService : IQBittorrentService
{
    private const int MaxFailedAttemptsPerCredential = 2;
    private const int AddVerificationMaxAttempts = 3;
    private static readonly TimeSpan AddVerificationDelay = TimeSpan.FromMilliseconds(400);
    private static readonly ConcurrentDictionary<string, CredentialFailureState> FailedAttemptsByCredential = new();
    private static readonly ConcurrentDictionary<string, EndpointSuspendState> SuspendedEndpoints = new();

    private readonly HttpClient _httpClient;
    private readonly ILogger<QBittorrentService> _logger;
    private readonly IOptionsMonitor<QBittorrentConfiguration> _config;

    private string _sessionCookie = string.Empty;
    private DateTime _sessionExpiry = DateTime.MinValue;
    private readonly SemaphoreSlim _loginLock = new(1, 1);

    private sealed record CredentialFailureState(int Failures, DateTime BlockedUntilUtc);
    private sealed record EndpointSuspendState(DateTime SuspendedUntilUtc, string Reason);
    private sealed record LoginAttemptResult(HttpStatusCode StatusCode, string Body, string? SessionCookie);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public QBittorrentService(
        HttpClient httpClient,
        ILogger<QBittorrentService> logger,
        IOptionsMonitor<QBittorrentConfiguration> config)
    {
        _httpClient = httpClient;
        _logger = logger;
        _config = config;

        _httpClient.Timeout = TimeSpan.FromSeconds(CurrentConfig.TimeoutSeconds);
    }

    private QBittorrentConfiguration CurrentConfig => _config.CurrentValue;
    private string BaseUrl => CurrentConfig.GetBaseUrl();

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            await EnsureAuthenticatedAsync();
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
        try
        {
            await EnsureAuthenticatedAsync();

            var formData = BuildAddOptions(savePath, category, paused);
            formData.Insert(0, new KeyValuePair<string, string>("urls", torrentUrl));

            var request = CreateRequest(HttpMethod.Post, "/api/v2/torrents/add");
            request.Content = new FormUrlEncodedContent(formData);

            var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (IsAddTorrentResponseSuccessful(response.StatusCode, responseBody))
            {
                _logger.LogInformation("Added torrent: {Url}", torrentUrl);
                return true;
            }

            _logger.LogWarning("Failed to add torrent. Status: {Status}, Response: {Response}",
                response.StatusCode, responseBody);
            return false;
        }
        catch (QBittorrentUnavailableException)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "qBittorrent add request timed out for torrent: {Url}", torrentUrl);
            throw MarkEndpointUnavailable(ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "qBittorrent add request failed for torrent: {Url}", torrentUrl);
            throw MarkEndpointUnavailable(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding torrent: {Url}", torrentUrl);
            return false;
        }
    }

    public async Task<List<QBTorrentInfo>> GetTorrentsAsync(string? category = null)
    {
        try
        {
            await EnsureAuthenticatedAsync();

            var url = "/api/v2/torrents/info";
            if (!string.IsNullOrEmpty(category))
            {
                url += $"?category={Uri.EscapeDataString(category)}";
            }

            var request = CreateRequest(HttpMethod.Get, url);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get torrents list. Status: {Status}", response.StatusCode);
                return new List<QBTorrentInfo>();
            }

            var json = await response.Content.ReadAsStringAsync();
            var torrents = JsonSerializer.Deserialize<List<QBTorrentInfo>>(json, JsonOptions) ?? new List<QBTorrentInfo>();
            foreach (var torrent in torrents)
            {
                torrent.Progress = NormalizeProgressPercent(torrent.Progress);
            }

            return torrents;
        }
        catch (QBittorrentUnavailableException)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "qBittorrent get-torrents request timed out");
            throw MarkEndpointUnavailable(ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "qBittorrent get-torrents request failed");
            throw MarkEndpointUnavailable(ex);
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

        try
        {
            var result = await SendTorrentActionAsync("/api/v2/torrents/pause", hash);
            if (result.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogWarning("qBittorrent endpoint /api/v2/torrents/pause returned 404, fallback to /api/v2/torrents/stop");
                result = await SendTorrentActionAsync("/api/v2/torrents/stop", hash);
            }

            if (IsHttpSuccessStatus(result.StatusCode))
            {
                _logger.LogInformation("Paused torrent: {Hash}", hash);
                return;
            }

            throw new InvalidOperationException(
                $"Failed to pause torrent {hash}. Status: {result.StatusCode}. Body: {result.Body}");
        }
        catch (QBittorrentUnavailableException)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "qBittorrent pause request timed out. Hash={Hash}", hash);
            throw MarkEndpointUnavailable(ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "qBittorrent pause request failed. Hash={Hash}", hash);
            throw MarkEndpointUnavailable(ex);
        }
    }

    public async Task ResumeTorrentAsync(string hash)
    {
        await EnsureAuthenticatedAsync();

        try
        {
            var locationReady = await TryEnsureTorrentLocationAsync(hash, CurrentConfig.DefaultSavePath);
            if (!locationReady)
            {
                throw new InvalidOperationException(
                    $"Failed to prepare save path for torrent {hash}. Target path: {CurrentConfig.DefaultSavePath}");
            }

            var result = await SendTorrentActionAsync("/api/v2/torrents/resume", hash);
            if (result.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogWarning("qBittorrent endpoint /api/v2/torrents/resume returned 404, fallback to /api/v2/torrents/start");
                result = await SendTorrentActionAsync("/api/v2/torrents/start", hash);
            }

            if (IsHttpSuccessStatus(result.StatusCode))
            {
                _logger.LogInformation("Resumed torrent: {Hash}", hash);
                return;
            }

            throw new InvalidOperationException(
                $"Failed to resume torrent {hash}. Status: {result.StatusCode}. Body: {result.Body}");
        }
        catch (QBittorrentUnavailableException)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "qBittorrent resume request timed out. Hash={Hash}", hash);
            throw MarkEndpointUnavailable(ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "qBittorrent resume request failed. Hash={Hash}", hash);
            throw MarkEndpointUnavailable(ex);
        }
    }

    public async Task<bool> DeleteTorrentAsync(string hash, bool deleteFiles = false)
    {
        try
        {
            await EnsureAuthenticatedAsync();

            var request = CreateRequest(HttpMethod.Post, "/api/v2/torrents/delete");
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("hashes", hash),
                new KeyValuePair<string, string>("deleteFiles", deleteFiles.ToString().ToLower())
            });

            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Deleted torrent: {Hash} (deleteFiles: {DeleteFiles})", hash, deleteFiles);
                return true;
            }
            else
            {
                _logger.LogWarning("Failed to delete torrent {Hash}. Status: {Status}", hash, response.StatusCode);
                return false;
            }
        }
        catch (QBittorrentUnavailableException)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "qBittorrent delete request timed out. Hash={Hash}", hash);
            throw MarkEndpointUnavailable(ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "qBittorrent delete request failed. Hash={Hash}", hash);
            throw MarkEndpointUnavailable(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting torrent: {Hash}", hash);
            return false;
        }
    }

    public async Task<bool> AddTorrentWithTrackingAsync(
        string torrentUrl,
        string torrentHash,
        string title,
        long fileSize,
        DownloadSource source,
        int? subscriptionId = null,
        string? animeTitle = null)
    {
        const string category = "anime";
        var normalizedHash = torrentHash.Trim().ToUpperInvariant();
        var baselineCount = await TryGetTorrentCountAsync(category);

        string? effectiveSavePath;
        if (CurrentConfig.UseAnimeSubPath
            && !string.IsNullOrWhiteSpace(animeTitle)
            && !string.IsNullOrWhiteSpace(CurrentConfig.DefaultSavePath))
        {
            var folder = SanitizeFolderName(animeTitle);
            effectiveSavePath = CurrentConfig.DefaultSavePath.TrimEnd('/', '\\') + "/" + folder;
        }
        else
        {
            effectiveSavePath = CurrentConfig.DefaultSavePath;
        }

        var success = await AddTorrentAsync(torrentUrl, savePath: effectiveSavePath, category: category);

        if (!success)
        {
            _logger.LogWarning("Failed to add torrent with tracking: Hash={Hash}, Source={Source}", torrentHash, source);
            return false;
        }

        var visibleInClient = await WaitUntilTorrentVisibleAsync(normalizedHash, baselineCount, category);
        if (!visibleInClient && IsHttpTorrentUrl(torrentUrl))
        {
            _logger.LogWarning(
                "qBittorrent add-by-url succeeded but torrent is not visible yet. Trying file-upload fallback. Hash={Hash}, Source={Source}",
                normalizedHash,
                source);

            var fallbackAdded = await AddTorrentByFileUploadAsync(torrentUrl, savePath: null, category: category, paused: null);
            if (fallbackAdded)
            {
                visibleInClient = await WaitUntilTorrentVisibleAsync(normalizedHash, baselineCount, category);
            }
        }

        if (!visibleInClient)
        {
            _logger.LogWarning(
                "qBittorrent add API returned success but torrent was not visible after verification window. Hash={Hash}, Source={Source}",
                normalizedHash,
                source);
            return false;
        }

        var locationReady = await TryEnsureTorrentLocationAsync(normalizedHash, effectiveSavePath);
        if (!locationReady)
        {
            _logger.LogWarning(
                "Torrent added but failed to apply configured save path. Hash={Hash}, TargetPath={TargetPath}, Source={Source}",
                normalizedHash,
                effectiveSavePath,
                source);
            return false;
        }

        _logger.LogInformation("Added torrent with tracking: Hash={Hash}, Source={Source}", normalizedHash, source);
        return true;
    }

    public async Task<int> SyncManualDownloadsProgressAsync()
    {
        try
        {
            await EnsureAuthenticatedAsync();
            var request = CreateRequest(HttpMethod.Get, "/api/v2/torrents/info");
            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get torrents for progress sync. Status: {Status}", response.StatusCode);
                return 0;
            }

            var json = await response.Content.ReadAsStringAsync();
            var torrents = JsonSerializer.Deserialize<List<QBTorrentInfo>>(json, JsonOptions) ?? new List<QBTorrentInfo>();

            _logger.LogInformation("Synced progress for {Count} torrents", torrents.Count);

            return torrents.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing download progress");
            return 0;
        }
    }

    #region Authentication

    private async Task EnsureAuthenticatedAsync()
    {
        ValidateAvailabilityPolicy();

        if (!string.IsNullOrEmpty(_sessionCookie) && DateTime.UtcNow < _sessionExpiry)
        {
            return;
        }

        ValidateCredentialPolicy();

        await _loginLock.WaitAsync();
        try
        {
            if (!string.IsNullOrEmpty(_sessionCookie) && DateTime.UtcNow < _sessionExpiry)
            {
                return;
            }

            ValidateAvailabilityPolicy();
            ValidateCredentialPolicy();
            await LoginAsync();
        }
        finally
        {
            _loginLock.Release();
        }
    }

    private async Task LoginAsync()
    {
        var credentialKey = BuildCredentialKey();
        var loginUrl = $"{BaseUrl}/api/v2/auth/login";

        try
        {
            var firstAttempt = await SendLoginRequestAsync(loginUrl);
            if (!IsLoginSuccessful(firstAttempt))
            {
                RecordFailedAttempt(credentialKey);
                _logger.LogError("qBittorrent login failed. Status: {Status}, Body: {Body}",
                    firstAttempt.StatusCode, firstAttempt.Body);
                throw new InvalidOperationException(
                    $"qBittorrent login failed with status {firstAttempt.StatusCode}. Body: {firstAttempt.Body}");
            }

            var sessionCookie = firstAttempt.SessionCookie;
            LoginAttemptResult? retryAttempt = null;

            if (string.IsNullOrWhiteSpace(sessionCookie))
            {
                _logger.LogWarning("qBittorrent login returned Ok but no SID cookie. Retrying once.");
                retryAttempt = await SendLoginRequestAsync(loginUrl);
                if (IsLoginSuccessful(retryAttempt))
                {
                    sessionCookie = retryAttempt.SessionCookie;
                }
            }

            if (string.IsNullOrWhiteSpace(sessionCookie) && !string.IsNullOrWhiteSpace(_sessionCookie))
            {
                if (await IsSessionStillValidAsync())
                {
                    sessionCookie = _sessionCookie;
                    _logger.LogWarning("Reusing existing qBittorrent session because login response did not include SID cookie.");
                }
            }

            if (string.IsNullOrWhiteSpace(sessionCookie))
            {
                RecordFailedAttempt(credentialKey);
                _logger.LogError(
                    "qBittorrent login succeeded but no session cookie received. FirstBody: {FirstBody}, RetryBody: {RetryBody}",
                    firstAttempt.Body,
                    retryAttempt?.Body ?? "<no-retry>");
                throw new InvalidOperationException("qBittorrent login succeeded but no session cookie received");
            }

            _sessionCookie = sessionCookie;
            _sessionExpiry = DateTime.UtcNow.AddMinutes(50);
            FailedAttemptsByCredential.TryRemove(credentialKey, out _);
            SuspendedEndpoints.TryRemove(BuildEndpointKey(), out _);
            _logger.LogInformation("Successfully authenticated with qBittorrent at {BaseUrl}", BaseUrl);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Failed to connect to qBittorrent at {BaseUrl}", BaseUrl);
            throw MarkEndpointUnavailable(ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to connect to qBittorrent at {BaseUrl}", BaseUrl);
            throw MarkEndpointUnavailable(ex);
        }
    }

    private async Task<LoginAttemptResult> SendLoginRequestAsync(string loginUrl)
    {
        using var formData = new FormUrlEncodedContent(new[]
    {
            new KeyValuePair<string, string>("username", CurrentConfig.Username),
            new KeyValuePair<string, string>("password", CurrentConfig.Password)
        });

        using var response = await _httpClient.PostAsync(loginUrl, formData);
        var body = await response.Content.ReadAsStringAsync();
        var sessionCookie = ExtractSessionCookie(response);
        return new LoginAttemptResult(response.StatusCode, body.Trim(), sessionCookie);
    }

    private static bool IsLoginSuccessful(LoginAttemptResult attempt)
    {
        return attempt.StatusCode == HttpStatusCode.OK &&
               string.Equals(attempt.Body, "Ok.", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> IsSessionStillValidAsync()
    {
        try
        {
            var request = CreateRequest(HttpMethod.Get, "/api/v2/app/version");
            using var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static string? ExtractSessionCookie(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            foreach (var cookie in cookies)
            {
                var trimmedCookie = cookie.Trim();
                if (trimmedCookie.StartsWith("SID=", StringComparison.OrdinalIgnoreCase))
                {
                    return trimmedCookie.Split(';', 2)[0];
                }
            }
        }
        return null;
    }

    private static bool IsAddTorrentResponseSuccessful(HttpStatusCode statusCode, string responseBody)
    {
        if (!IsHttpSuccessStatus(statusCode))
        {
            return false;
        }

        var body = responseBody.Trim();
        if (string.IsNullOrEmpty(body))
        {
            return true;
        }

        return !string.Equals(body, "Fails.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHttpSuccessStatus(HttpStatusCode statusCode)
    {
        var numeric = (int)statusCode;
        return numeric >= 200 && numeric < 300;
    }

    private async Task<(HttpStatusCode StatusCode, string Body)> SendTorrentActionAsync(string endpoint, string hash)
    {
        var request = CreateRequest(HttpMethod.Post, endpoint);
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("hashes", hash)
        });

        using var response = await _httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, body);
    }

    private async Task<bool> AddTorrentByFileUploadAsync(string torrentUrl, string? savePath, string? category, bool? paused)
    {
        try
        {
            await EnsureAuthenticatedAsync();

            using var sourceResponse = await _httpClient.GetAsync(torrentUrl);
            if (!sourceResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Failed to download torrent file for fallback upload. Url={Url}, Status={Status}",
                    torrentUrl,
                    sourceResponse.StatusCode);
                return false;
            }

            var payload = await sourceResponse.Content.ReadAsByteArrayAsync();
            if (payload.Length == 0)
            {
                _logger.LogWarning("Downloaded torrent file is empty. Url={Url}", torrentUrl);
                return false;
            }

            var filename = TryResolveTorrentFilename(torrentUrl);
            using var multipart = new MultipartFormDataContent();
            using var fileContent = new ByteArrayContent(payload);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-bittorrent");
            multipart.Add(fileContent, "torrents", filename);

            var options = BuildAddOptions(savePath, category, paused);
            foreach (var option in options)
            {
                multipart.Add(new StringContent(option.Value), option.Key);
            }

            var request = CreateRequest(HttpMethod.Post, "/api/v2/torrents/add");
            request.Content = multipart;

            var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (IsAddTorrentResponseSuccessful(response.StatusCode, responseBody))
            {
                _logger.LogInformation("Added torrent by file-upload fallback: {Url}", torrentUrl);
                return true;
            }

            _logger.LogWarning(
                "File-upload fallback failed. Url={Url}, Status={Status}, Response={Response}",
                torrentUrl,
                response.StatusCode,
                responseBody);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in file-upload fallback for torrent: {Url}", torrentUrl);
            return false;
        }
    }

    private async Task<bool> TryEnsureTorrentLocationAsync(string hash, string? savePath)
    {
        if (string.IsNullOrWhiteSpace(hash) || string.IsNullOrWhiteSpace(savePath))
        {
            return true;
        }

        try
        {
            var request = CreateRequest(HttpMethod.Post, "/api/v2/torrents/setLocation");
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("hashes", hash),
                new KeyValuePair<string, string>("location", savePath)
            });

            using var response = await _httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (IsHttpSuccessStatus(response.StatusCode))
            {
                _logger.LogInformation("Ensured torrent save path. Hash={Hash}, SavePath={SavePath}", hash, savePath);
                return true;
            }

            _logger.LogWarning(
                "Failed to set torrent location. Hash={Hash}, SavePath={SavePath}, Status={Status}, Body={Body}",
                hash,
                savePath,
                response.StatusCode,
                body);
            return false;
        }
        catch (QBittorrentUnavailableException)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "qBittorrent set-location request timed out. Hash={Hash}", hash);
            throw MarkEndpointUnavailable(ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "qBittorrent set-location request failed. Hash={Hash}", hash);
            throw MarkEndpointUnavailable(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting torrent location. Hash={Hash}, SavePath={SavePath}", hash, savePath);
            return false;
        }
    }

    private async Task<bool> WaitUntilTorrentVisibleAsync(string hash, int? baselineCount, string? category)
    {
        for (var attempt = 0; attempt < AddVerificationMaxAttempts; attempt++)
        {
            var torrents = await GetTorrentsAsync(category);
            var hashMatched = !string.IsNullOrWhiteSpace(hash) &&
                              torrents.Any(t => string.Equals(t.Hash, hash, StringComparison.OrdinalIgnoreCase));

            if (hashMatched)
            {
                return true;
            }

            if (baselineCount.HasValue && torrents.Count > baselineCount.Value)
            {
                // Hash can differ for some torrent variants; count growth in target category indicates task was accepted.
                return true;
            }

            if (attempt < AddVerificationMaxAttempts - 1)
            {
                await Task.Delay(AddVerificationDelay);
            }
        }

        return false;
    }

    private static bool IsHttpTorrentUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static string SanitizeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
    }

    private static string TryResolveTorrentFilename(string torrentUrl)
    {
        if (Uri.TryCreate(torrentUrl, UriKind.Absolute, out var uri))
        {
            var filename = System.IO.Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(filename))
            {
                return filename;
            }
        }

        return "download.torrent";
    }

    private async Task<int?> TryGetTorrentCountAsync(string? category)
    {
        try
        {
            var torrents = await GetTorrentsAsync(category);
            return torrents.Count;
        }
        catch (QBittorrentUnavailableException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private List<KeyValuePair<string, string>> BuildAddOptions(string? savePath, string? category, bool? paused)
    {
        var formData = new List<KeyValuePair<string, string>>();

        var actualSavePath = savePath ?? CurrentConfig.DefaultSavePath;
        var actualCategory = category ?? CurrentConfig.Category;
        var actualTags = CurrentConfig.Tags;
        var actualPaused = paused ?? CurrentConfig.PauseTorrentAfterAdd;

        if (!string.IsNullOrEmpty(actualSavePath))
        {
            formData.Add(new KeyValuePair<string, string>("savepath", actualSavePath));
        }

        if (!string.IsNullOrEmpty(actualCategory))
        {
            formData.Add(new KeyValuePair<string, string>("category", actualCategory));
        }

        if (!string.IsNullOrWhiteSpace(actualTags))
        {
            formData.Add(new KeyValuePair<string, string>("tags", actualTags));
        }

        if (actualPaused)
        {
            formData.Add(new KeyValuePair<string, string>("paused", "true"));
        }

        return formData;
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

    private void ValidateCredentialPolicy()
    {
        if (string.IsNullOrWhiteSpace(CurrentConfig.Username) || string.IsNullOrWhiteSpace(CurrentConfig.Password))
        {
            throw new InvalidOperationException("qBittorrent credentials are missing. Username and password are required.");
        }

        var credentialKey = BuildCredentialKey();
        if (!FailedAttemptsByCredential.TryGetValue(credentialKey, out var state))
        {
            return;
        }

        if (state.Failures < MaxFailedAttemptsPerCredential)
        {
            return;
        }

        if (state.BlockedUntilUtc <= DateTime.UtcNow)
        {
            FailedAttemptsByCredential.TryRemove(credentialKey, out _);
            return;
        }

        throw new InvalidOperationException(
            $"qBittorrent login is blocked for current credentials after {state.Failures} failed attempts. " +
            $"Retry after {state.BlockedUntilUtc:O} UTC.");
    }

    private TimeSpan GetFailedLoginBlockDuration()
    {
        var seconds = CurrentConfig.FailedLoginBlockSeconds;
        if (seconds <= 0)
        {
            seconds = 300;
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private TimeSpan GetEndpointSuspendDuration()
    {
        var seconds = CurrentConfig.OfflineSuspendSeconds;
        if (seconds <= 0)
        {
            seconds = Math.Max(30, CurrentConfig.TimeoutSeconds);
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private string BuildCredentialKey()
    {
        var raw = $"{BaseUrl}|{CurrentConfig.Username}|{CurrentConfig.Password}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }

    private string BuildEndpointKey()
    {
        return BaseUrl.Trim().ToLowerInvariant();
    }

    private void RecordFailedAttempt(string credentialKey)
    {
        FailedAttemptsByCredential.AddOrUpdate(
            credentialKey,
            _ => new CredentialFailureState(1, DateTime.MinValue),
            (_, current) =>
            {
                var failures = current.Failures + 1;
                var blockedUntil = failures >= MaxFailedAttemptsPerCredential
                    ? DateTime.UtcNow.Add(GetFailedLoginBlockDuration())
                    : DateTime.MinValue;
                return new CredentialFailureState(failures, blockedUntil);
            });
    }

    private void ValidateAvailabilityPolicy()
    {
        var endpointKey = BuildEndpointKey();
        if (!SuspendedEndpoints.TryGetValue(endpointKey, out var state))
        {
            return;
        }

        if (state.SuspendedUntilUtc <= DateTime.UtcNow)
        {
            SuspendedEndpoints.TryRemove(endpointKey, out _);
            return;
        }

        throw CreateUnavailableException(state.Reason, state.SuspendedUntilUtc);
    }

    private QBittorrentUnavailableException MarkEndpointUnavailable(Exception ex)
    {
        var endpointKey = BuildEndpointKey();
        var reason = BuildEndpointFailureReason(ex);
        var blockedUntil = DateTime.UtcNow.Add(GetEndpointSuspendDuration());

        var state = SuspendedEndpoints.AddOrUpdate(
            endpointKey,
            _ => new EndpointSuspendState(blockedUntil, reason),
            (_, current) =>
            {
                if (current.SuspendedUntilUtc > blockedUntil)
                {
                    return current;
                }

                return new EndpointSuspendState(blockedUntil, reason);
            });

        return CreateUnavailableException(state.Reason, state.SuspendedUntilUtc, ex);
    }

    private QBittorrentUnavailableException CreateUnavailableException(
        string reason,
        DateTime retryAfterUtc,
        Exception? innerException = null)
    {
        var message = $"qBittorrent is offline or unreachable. {reason} Retry after {retryAfterUtc:O} UTC.";
        return new QBittorrentUnavailableException(message, reason, retryAfterUtc, innerException);
    }

    private string BuildEndpointFailureReason(Exception exception)
    {
        if (exception is TaskCanceledException)
        {
            return $"qBittorrent API request timed out after {CurrentConfig.TimeoutSeconds} seconds.";
        }

        if (exception is HttpRequestException httpEx)
        {
            if (httpEx.InnerException is SocketException socketEx)
            {
                return $"Network error ({socketEx.SocketErrorCode}): {socketEx.Message}";
            }

            if (!string.IsNullOrWhiteSpace(httpEx.Message))
            {
                return httpEx.Message;
            }
        }

        if (!string.IsNullOrWhiteSpace(exception.Message))
        {
            return exception.Message;
        }

        return "Unknown network error";
    }

    private static double NormalizeProgressPercent(double rawProgress)
    {
        if (double.IsNaN(rawProgress) || double.IsInfinity(rawProgress))
        {
            return 0;
        }

        var percent = rawProgress <= 1 ? rawProgress * 100 : rawProgress;
        return Math.Round(Math.Clamp(percent, 0, 100), 2);
    }

    #endregion
}
