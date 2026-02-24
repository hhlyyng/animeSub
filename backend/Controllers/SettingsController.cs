using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using backend.Data;
using backend.Services;
using backend.Services.Exceptions;
using backend.Services.Validators;

namespace backend.Controllers;

/// <summary>
/// API controller for managing application settings (tokens, qBittorrent, mikan polling and download preferences)
/// </summary>
[ApiController]
[Route("api/settings")]
[Authorize]
public class SettingsController : ControllerBase
{
    private static readonly SemaphoreSlim RuntimeSettingsFileLock = new(1, 1);
    private const string RuntimeSettingsFileName = "appsettings.runtime.json";
    private const string DefaultQbCategory = "anime";
    private const string DefaultQbTags = "AnimeSub";
    private const int MinPollingIntervalMinutes = 1;
    private const int MaxPollingIntervalMinutes = 1440;
    private const string DefaultSubgroupPreference = "all";
    private const string DefaultResolutionPreference = "1080P";
    private const string DefaultSubtitleTypePreference = "\u7b80\u65e5\u5185\u5d4c";
    private static readonly string[] DownloadPreferenceFields = ["subgroup", "resolution", "subtitleType"];
    private const int MinSubgroupAirYear = 2025;
    private static readonly Regex SubgroupNoiseTokenRegex = new(
        @"\b(?:2160p|1080p|720p|4k|x26[45]|hevc|av1|aac|flac|mp4|mkv|chs|cht)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SubgroupEpisodeHintRegex = new(
        @"(?:\bS\d{1,2}\b|\bE\d{1,3}\b|\bEP?\s*\d{1,3}\b|\u7b2c\s*\d+\s*[\u8bdd\u8a71\u96c6]|Season\s*\d+|After\s*Story|\u5267\u573a\u7248|\u5408\u96c6|\u5b8c\u7ed3)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ITokenStorageService _tokenStorage;
    private readonly TokenValidator _tokenValidator;
    private readonly ILogger<SettingsController> _logger;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AnimeDbContext _dbContext;

    public SettingsController(
        ITokenStorageService tokenStorage,
        TokenValidator tokenValidator,
        ILogger<SettingsController> logger,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        IHttpClientFactory httpClientFactory,
        AnimeDbContext dbContext)
    {
        _tokenStorage = tokenStorage;
        _tokenValidator = tokenValidator;
        _logger = logger;
        _configuration = configuration;
        _environment = environment;
        _httpClientFactory = httpClientFactory;
        _dbContext = dbContext;
    }

    /// <summary>
    /// Get current token configuration status (masked)
    /// GET /api/settings/tokens
    /// </summary>
    [HttpGet("tokens")]
    public async Task<IActionResult> GetTokenStatus()
    {
        try
        {
            var tmdbToken = await _tokenStorage.GetTmdbTokenAsync();
            var hasTmdb = !string.IsNullOrWhiteSpace(tmdbToken);

            return Ok(new
            {
                tmdb = new
                {
                    configured = hasTmdb,
                    preview = hasTmdb ? MaskToken(tmdbToken) : null
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get token status");
            return StatusCode(500, new { error = "Failed to retrieve token status" });
        }
    }

    /// <summary>
    /// Update TMDB token only
    /// PUT /api/settings/tokens
    /// Body: { "tmdbToken": "xxx" }
    /// </summary>
    [HttpPut("tokens")]
    public async Task<IActionResult> UpdateTokens([FromBody] UpdateTokensRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(request.TmdbToken))
            {
                _tokenValidator.ValidateTmdbToken(request.TmdbToken);
            }

            await _tokenStorage.SaveTmdbTokenAsync(request.TmdbToken);

            if (!string.IsNullOrWhiteSpace(request.TmdbToken))
            {
                await SaveRuntimeSettingsAsync(root =>
                {
                    var apiTokens = EnsureSection(root, "ApiTokens");
                    apiTokens["TmdbToken"] = request.TmdbToken.Trim();
                }, cancellationToken);
            }

            _logger.LogInformation("TMDB token updated successfully");
            return Ok(new
            {
                message = "Token updated successfully",
                tmdb = new { configured = !string.IsNullOrWhiteSpace(request.TmdbToken) }
            });
        }
        catch (InvalidCredentialsException ex)
        {
            _logger.LogWarning("Token validation failed: {Message}", ex.Message);
            return BadRequest(new { error = ex.Message, field = ex.CredentialType });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update token");
            return StatusCode(500, new { error = "Failed to save token" });
        }
    }

    /// <summary>
    /// Delete stored TMDB token
    /// DELETE /api/settings/tokens
    /// </summary>
    [HttpDelete("tokens")]
    public async Task<IActionResult> DeleteTokens(CancellationToken cancellationToken)
    {
        try
        {
            await _tokenStorage.SaveTmdbTokenAsync(null);
            await SaveRuntimeSettingsAsync(root =>
            {
                var apiTokens = EnsureSection(root, "ApiTokens");
                apiTokens["TmdbToken"] = string.Empty;
            }, cancellationToken);

            _logger.LogInformation("TMDB token deleted");
            return Ok(new { message = "Token deleted successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete token");
            return StatusCode(500, new { error = "Failed to delete token" });
        }
    }

    /// <summary>
    /// Get settings profile for settings page (sensitive fields masked)
    /// GET /api/settings/profile
    /// </summary>
    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile()
    {
        try
        {
            var tmdbToken = await _tokenStorage.GetTmdbTokenAsync();

            var host = (_configuration["QBittorrent:Host"] ?? "localhost").Trim();
            var port = ParseIntOrDefault(_configuration["QBittorrent:Port"], 8080);
            var username = (_configuration["QBittorrent:Username"] ?? string.Empty).Trim();
            var password = _configuration["QBittorrent:Password"] ?? string.Empty;
            var defaultSavePath = (_configuration["QBittorrent:DefaultSavePath"] ?? string.Empty).Trim();
            var category = (_configuration["QBittorrent:Category"] ?? DefaultQbCategory).Trim();
            var tags = (_configuration["QBittorrent:Tags"] ?? DefaultQbTags).Trim();
            var animeSubUsername = (_configuration["AnimeSub:Username"] ?? string.Empty).Trim();
            var animeSubPassword = _configuration["AnimeSub:Password"] ?? string.Empty;
            var pollingInterval = ParseIntOrDefault(_configuration["Mikan:PollingIntervalMinutes"], 30);
            pollingInterval = Math.Clamp(pollingInterval, MinPollingIntervalMinutes, MaxPollingIntervalMinutes);

            var subgroupOptions = await LoadDistinctSubgroupValuesAsync();
            var resolutionOptions = await LoadDistinctFeedValuesAsync(item => item.Resolution);
            var subtitleTypeOptions = await LoadDistinctFeedValuesAsync(item => item.SubtitleType);

            var subgroup = ReadPreferenceValue("DownloadPreferences:Subgroup", DefaultSubgroupPreference);
            var resolution = ReadPreferenceValue("DownloadPreferences:Resolution", DefaultResolutionPreference);
            var subtitleType = ReadPreferenceValue("DownloadPreferences:SubtitleType", DefaultSubtitleTypePreference);
            var priorityOrder = ReadPriorityOrderFromConfiguration();

            EnsureContainsIgnoreCase(subgroupOptions, DefaultSubgroupPreference, insertAtBeginning: true);
            EnsureContainsIgnoreCase(resolutionOptions, DefaultResolutionPreference, insertAtBeginning: true);
            EnsureContainsIgnoreCase(subtitleTypeOptions, DefaultSubtitleTypePreference, insertAtBeginning: true);
            EnsureContainsIgnoreCase(subgroupOptions, subgroup);
            EnsureContainsIgnoreCase(resolutionOptions, resolution);
            EnsureContainsIgnoreCase(subtitleTypeOptions, subtitleType);

            return Ok(new SettingsProfileResponse(
                Tmdb: new SettingsTokenStatusDto(
                    Configured: !string.IsNullOrWhiteSpace(tmdbToken),
                    Preview: string.IsNullOrWhiteSpace(tmdbToken) ? null : MaskToken(tmdbToken)),
                Qbittorrent: new QbittorrentSettingsDto(
                    Host: host,
                    Port: port,
                    Username: username,
                    PasswordConfigured: !string.IsNullOrWhiteSpace(password),
                    DefaultSavePath: defaultSavePath,
                    Category: string.IsNullOrWhiteSpace(category) ? DefaultQbCategory : category,
                    Tags: string.IsNullOrWhiteSpace(tags) ? DefaultQbTags : tags,
                    UseAnimeSubPath: _configuration.GetValue<bool>("QBittorrent:UseAnimeSubPath")),
                AnimeSub: new AnimeSubSettingsDto(
                    Username: animeSubUsername,
                    PasswordConfigured: !string.IsNullOrWhiteSpace(animeSubPassword)),
                Mikan: new MikanSettingsDto(PollingIntervalMinutes: pollingInterval),
                DownloadPreferences: new DownloadPreferencesSettingsDto(
                    Subgroup: subgroup,
                    Resolution: resolution,
                    SubtitleType: subtitleType,
                    PriorityOrder: priorityOrder),
                DownloadPreferenceOptions: new DownloadPreferenceOptionsDto(
                    Subgroups: subgroupOptions,
                    Resolutions: resolutionOptions,
                    SubtitleTypes: subtitleTypeOptions,
                    PriorityFields: DownloadPreferenceFields)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get settings profile");
            return StatusCode(500, new { error = "Failed to retrieve settings profile" });
        }
    }

    /// <summary>
    /// Save settings profile
    /// PUT /api/settings/profile
    /// </summary>
    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateSettingsProfileRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.Qbittorrent is null)
            {
                return BadRequest(new { error = "QBittorrent settings are required" });
            }

            var host = request.Qbittorrent.Host?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(host))
            {
                return BadRequest(new { error = "QBittorrent host is required", field = "qbittorrent.host" });
            }

            if (!request.Qbittorrent.Port.HasValue || request.Qbittorrent.Port.Value < 1 || request.Qbittorrent.Port.Value > 65535)
            {
                return BadRequest(new { error = "QBittorrent port must be between 1 and 65535", field = "qbittorrent.port" });
            }
            var port = request.Qbittorrent.Port.Value;

            var defaultSavePath = request.Qbittorrent.DefaultSavePath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(defaultSavePath))
            {
                return BadRequest(new { error = "Default save path is required", field = "qbittorrent.defaultSavePath" });
            }

            var category = string.IsNullOrWhiteSpace(request.Qbittorrent.Category)
                ? DefaultQbCategory
                : request.Qbittorrent.Category.Trim();
            var tags = string.IsNullOrWhiteSpace(request.Qbittorrent.Tags)
                ? DefaultQbTags
                : request.Qbittorrent.Tags.Trim();
            var animeSubUsername = request.AnimeSub?.Username?.Trim()
                ?? (_configuration["AnimeSub:Username"] ?? string.Empty).Trim();
            var existingAnimeSubPassword = _configuration["AnimeSub:Password"] ?? string.Empty;
            var animeSubPassword = string.IsNullOrWhiteSpace(request.AnimeSub?.Password)
                ? existingAnimeSubPassword
                : request.AnimeSub.Password!;

            var existingUsername = (_configuration["QBittorrent:Username"] ?? string.Empty).Trim();
            var username = string.IsNullOrWhiteSpace(request.Qbittorrent.Username)
                ? existingUsername
                : request.Qbittorrent.Username.Trim();
            if (string.IsNullOrWhiteSpace(username))
            {
                return BadRequest(new { error = "QBittorrent username is required", field = "qbittorrent.username" });
            }

            var existingPassword = _configuration["QBittorrent:Password"] ?? string.Empty;
            var password = string.IsNullOrWhiteSpace(request.Qbittorrent.Password)
                ? existingPassword
                : request.Qbittorrent.Password;
            if (string.IsNullOrWhiteSpace(password))
            {
                return BadRequest(new { error = "QBittorrent password is required", field = "qbittorrent.password" });
            }

            var pollingInterval = ParseIntOrDefault(_configuration["Mikan:PollingIntervalMinutes"], 30);
            pollingInterval = Math.Clamp(pollingInterval, MinPollingIntervalMinutes, MaxPollingIntervalMinutes);
            if (request.Mikan?.PollingIntervalMinutes is int requestedPollingInterval)
            {
                if (requestedPollingInterval < MinPollingIntervalMinutes || requestedPollingInterval > MaxPollingIntervalMinutes)
                {
                    return BadRequest(new
                    {
                        error = $"Polling interval must be between {MinPollingIntervalMinutes} and {MaxPollingIntervalMinutes} minutes",
                        field = "mikan.pollingIntervalMinutes"
                    });
                }
                pollingInterval = requestedPollingInterval;
            }

            var hasNewTmdbToken = !string.IsNullOrWhiteSpace(request.TmdbToken);
            var newTmdbToken = hasNewTmdbToken ? request.TmdbToken!.Trim() : null;
            var existingTmdbToken = await _tokenStorage.GetTmdbTokenAsync();
            var effectiveTmdbToken = hasNewTmdbToken
                ? newTmdbToken!
                : (existingTmdbToken?.Trim() ?? string.Empty);

            if (string.IsNullOrWhiteSpace(effectiveTmdbToken))
            {
                return BadRequest(new { error = "TMDB token is required", field = "tmdbToken" });
            }

            if (hasNewTmdbToken)
            {
                _tokenValidator.ValidateTmdbToken(request.TmdbToken);
                await _tokenStorage.SaveTmdbTokenAsync(request.TmdbToken);
            }

            var subgroup = string.IsNullOrWhiteSpace(request.DownloadPreferences?.Subgroup)
                ? ReadPreferenceValue("DownloadPreferences:Subgroup", DefaultSubgroupPreference)
                : request.DownloadPreferences.Subgroup.Trim();
            var resolution = string.IsNullOrWhiteSpace(request.DownloadPreferences?.Resolution)
                ? ReadPreferenceValue("DownloadPreferences:Resolution", DefaultResolutionPreference)
                : request.DownloadPreferences.Resolution.Trim();
            var subtitleType = string.IsNullOrWhiteSpace(request.DownloadPreferences?.SubtitleType)
                ? ReadPreferenceValue("DownloadPreferences:SubtitleType", DefaultSubtitleTypePreference)
                : request.DownloadPreferences.SubtitleType.Trim();
            var priorityOrder = request.DownloadPreferences?.PriorityOrder is null
                ? ReadPriorityOrderFromConfiguration()
                : ValidatePriorityOrder(request.DownloadPreferences.PriorityOrder);

            await SaveRuntimeSettingsAsync(root =>
            {
                if (hasNewTmdbToken)
                {
                    var apiTokens = EnsureSection(root, "ApiTokens");
                    apiTokens["TmdbToken"] = newTmdbToken;
                }

                var qb = EnsureSection(root, "QBittorrent");
                qb["Host"] = host;
                qb["Port"] = port;
                qb["Username"] = username;
                qb["Password"] = password;
                qb["DefaultSavePath"] = defaultSavePath;
                qb["Category"] = category;
                qb["Tags"] = tags;
                qb["UseAnimeSubPath"] = request.Qbittorrent.UseAnimeSubPath
                    ?? _configuration.GetValue<bool>("QBittorrent:UseAnimeSubPath");

                var animeSub = EnsureSection(root, "AnimeSub");
                animeSub["Username"] = animeSubUsername;
                animeSub["Password"] = animeSubPassword;

                var mikan = EnsureSection(root, "Mikan");
                mikan["PollingIntervalMinutes"] = pollingInterval;

                var downloadPreferences = EnsureSection(root, "DownloadPreferences");
                downloadPreferences["Subgroup"] = subgroup;
                downloadPreferences["Resolution"] = resolution;
                downloadPreferences["SubtitleType"] = subtitleType;
                var priorityArray = new JsonArray();
                foreach (var field in priorityOrder)
                {
                    priorityArray.Add(field);
                }
                downloadPreferences["PriorityOrder"] = priorityArray;
            }, cancellationToken);

            _logger.LogInformation("Settings profile updated successfully");
            return Ok(new { message = "Settings updated successfully" });
        }
        catch (InvalidCredentialsException ex)
        {
            _logger.LogWarning("Settings validation failed: {Message}", ex.Message);
            return BadRequest(new { error = ex.Message, field = ex.CredentialType });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update settings profile");
            return StatusCode(500, new { error = "Failed to update settings profile" });
        }
    }

    /// <summary>
    /// Test TMDB token
    /// POST /api/settings/test/tmdb
    /// </summary>
    [HttpPost("test/tmdb")]
    [AllowAnonymous]
    public async Task<IActionResult> TestTmdbToken([FromBody] TestTmdbTokenRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TmdbToken))
        {
            return BadRequest(new SettingsTestResponse(false, "TMDB token is required"));
        }

        try
        {
            _tokenValidator.ValidateTmdbToken(request.TmdbToken);

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);

            using var message = new HttpRequestMessage(HttpMethod.Get, "https://api.themoviedb.org/3/configuration");
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.TmdbToken.Trim());
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await client.SendAsync(message, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return Ok(new SettingsTestResponse(true, "TMDB token is valid"));
            }

            return BadRequest(new SettingsTestResponse(false, $"TMDB API validation failed ({(int)response.StatusCode})"));
        }
        catch (InvalidCredentialsException ex)
        {
            return BadRequest(new SettingsTestResponse(false, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TMDB token connectivity test failed");
            return BadRequest(new SettingsTestResponse(false, "Failed to reach TMDB API"));
        }
    }

    /// <summary>
    /// Test qBittorrent related field
    /// POST /api/settings/test/qbittorrent
    /// </summary>
    [HttpPost("test/qbittorrent")]
    [AllowAnonymous]
    public async Task<IActionResult> TestQbittorrent([FromBody] TestQbittorrentRequest request, CancellationToken cancellationToken)
    {
        var field = (request.Field ?? string.Empty).Trim().ToLowerInvariant();

        switch (field)
        {
            case "host":
                if (string.IsNullOrWhiteSpace(request.Host))
                {
                    return BadRequest(new SettingsTestResponse(false, "Host is required"));
                }
                break;
            case "port":
                if (!request.Port.HasValue || request.Port.Value < 1 || request.Port.Value > 65535)
                {
                    return BadRequest(new SettingsTestResponse(false, "Port must be between 1 and 65535"));
                }
                break;
            case "username":
                {
                    var usernameForValidation = string.IsNullOrWhiteSpace(request.Username)
                        ? (_configuration["QBittorrent:Username"] ?? string.Empty).Trim()
                        : request.Username.Trim();
                    if (string.IsNullOrWhiteSpace(usernameForValidation))
                    {
                        return BadRequest(new SettingsTestResponse(false, "Username is required"));
                    }
                }
                break;
            case "password":
                {
                    var passwordForValidation = string.IsNullOrWhiteSpace(request.Password)
                        ? (_configuration["QBittorrent:Password"] ?? string.Empty)
                        : request.Password;
                    if (string.IsNullOrWhiteSpace(passwordForValidation))
                    {
                        return BadRequest(new SettingsTestResponse(false, "Password is required"));
                    }
                }
                break;
            case "defaultsavepath":
                if (string.IsNullOrWhiteSpace(request.DefaultSavePath))
                {
                    return BadRequest(new SettingsTestResponse(false, "Default save path is required"));
                }
                break;
            case "category":
                if (string.IsNullOrWhiteSpace(request.Category))
                {
                    return Ok(new SettingsTestResponse(true, $"Category will use default: {DefaultQbCategory}"));
                }
                break;
            case "tags":
                if (string.IsNullOrWhiteSpace(request.Tags))
                {
                    return Ok(new SettingsTestResponse(true, $"Tags will use default: {DefaultQbTags}"));
                }
                break;
            default:
                return BadRequest(new SettingsTestResponse(false, "Unknown field"));
        }

        // For connection-related fields, try real connectivity test when credentials are provided.
        if (field is "host" or "port" or "username" or "password")
        {
            if (string.IsNullOrWhiteSpace(request.Host) ||
                !request.Port.HasValue ||
                request.Port.Value < 1 ||
                request.Port.Value > 65535)
            {
                return Ok(new SettingsTestResponse(true, "Field format is valid"));
            }

            var resolvedUsername = string.IsNullOrWhiteSpace(request.Username)
                ? (_configuration["QBittorrent:Username"] ?? string.Empty).Trim()
                : request.Username.Trim();
            var resolvedPassword = string.IsNullOrWhiteSpace(request.Password)
                ? (_configuration["QBittorrent:Password"] ?? string.Empty)
                : request.Password;

            if (string.IsNullOrWhiteSpace(resolvedUsername) || string.IsNullOrWhiteSpace(resolvedPassword))
            {
                return BadRequest(new SettingsTestResponse(false, "Username and password are required for qBittorrent connection test"));
            }

            var (success, message) = await TryTestQbConnectionAsync(
                request.Host.Trim(),
                request.Port.Value,
                resolvedUsername,
                resolvedPassword,
                cancellationToken);

            if (!success)
            {
                return BadRequest(new SettingsTestResponse(false, message));
            }

            return Ok(new SettingsTestResponse(true, message));
        }

        return Ok(new SettingsTestResponse(true, "Field format is valid"));
    }

    /// <summary>
    /// Test mikan polling interval
    /// POST /api/settings/test/mikan-polling
    /// </summary>
    [HttpPost("test/mikan-polling")]
    [AllowAnonymous]
    public IActionResult TestMikanPolling([FromBody] TestMikanPollingRequest request)
    {
        if (!request.PollingIntervalMinutes.HasValue)
        {
            return BadRequest(new SettingsTestResponse(false, "Polling interval is required"));
        }

        if (request.PollingIntervalMinutes.Value < MinPollingIntervalMinutes ||
            request.PollingIntervalMinutes.Value > MaxPollingIntervalMinutes)
        {
            return BadRequest(new SettingsTestResponse(
                false,
                $"Polling interval must be between {MinPollingIntervalMinutes} and {MaxPollingIntervalMinutes} minutes"));
        }

        return Ok(new SettingsTestResponse(true, "Polling interval is valid"));
    }

    private async Task<(bool success, string message)> TryTestQbConnectionAsync(
        string host,
        int port,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);

            var baseUrl = $"http://{host}:{port}";
            using var formData = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("username", username),
                new KeyValuePair<string, string>("password", password)
            });

            using var response = await client.PostAsync($"{baseUrl}/api/v2/auth/login", formData, cancellationToken);
            var body = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
            var loginOk = response.StatusCode == HttpStatusCode.OK &&
                          string.Equals(body, "Ok.", StringComparison.OrdinalIgnoreCase);

            if (!loginOk)
            {
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    return (false, "qBittorrent is reachable, but username or password is incorrect");
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return (false, "qBittorrent is reachable, but /api/v2/auth/login is not available");
                }

                var bodyHint = string.IsNullOrWhiteSpace(body) ? string.Empty : $": {body}";
                return (false, $"qBittorrent login test failed ({(int)response.StatusCode}){bodyHint}");
            }

            return (true, "qBittorrent connection is valid");
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "qBittorrent connectivity test timed out");
            return (false, "qBittorrent endpoint timed out (host reachable but did not respond in time)");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "qBittorrent connectivity request failed");
            return (false, $"Failed to reach qBittorrent host/port: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "qBittorrent connectivity test failed");
            return (false, "Failed to connect to qBittorrent endpoint");
        }
    }

    private async Task SaveRuntimeSettingsAsync(Action<JsonObject> mutate, CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(_environment.ContentRootPath, RuntimeSettingsFileName);

        await RuntimeSettingsFileLock.WaitAsync(cancellationToken);
        try
        {
            JsonObject root;
            if (System.IO.File.Exists(filePath))
            {
                try
                {
                    var existing = await System.IO.File.ReadAllTextAsync(filePath, cancellationToken);
                    root = JsonNode.Parse(existing) as JsonObject ?? new JsonObject();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse runtime settings file, recreating it");
                    root = new JsonObject();
                }
            }
            else
            {
                root = new JsonObject();
            }

            mutate(root);

            var json = root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await System.IO.File.WriteAllTextAsync(filePath, json, cancellationToken);
        }
        finally
        {
            RuntimeSettingsFileLock.Release();
        }
    }

    private static JsonObject EnsureSection(JsonObject root, string sectionName)
    {
        if (root[sectionName] is JsonObject existing)
        {
            return existing;
        }

        var section = new JsonObject();
        root[sectionName] = section;
        return section;
    }

    private static int ParseIntOrDefault(string? value, int defaultValue)
    {
        return int.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    private async Task<List<string>> LoadDistinctFeedValuesAsync(Expression<Func<backend.Data.Entities.MikanFeedItemEntity, string?>> selector)
    {
        var values = await _dbContext.MikanFeedItems
            .AsNoTracking()
            .Select(selector)
            .Where(value => value != null && value.Trim().Length > 0)
            .Select(value => value!.Trim())
            .ToListAsync();

        return values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<List<string>> LoadDistinctSubgroupValuesAsync()
    {
        var animeAirDateRows = await _dbContext.AnimeInfos
            .AsNoTracking()
            .Where(anime => anime.MikanBangumiId != null && anime.AirDate != null)
            .Select(anime => new
            {
                MikanBangumiId = anime.MikanBangumiId!,
                AirDate = anime.AirDate!
            })
            .ToListAsync();

        var eligibleMikanIds = animeAirDateRows
            .Where(row =>
                !string.IsNullOrWhiteSpace(row.MikanBangumiId) &&
                TryParseAirYear(row.AirDate, out var year) &&
                year >= MinSubgroupAirYear)
            .Select(row => row.MikanBangumiId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (eligibleMikanIds.Count == 0)
        {
            return new List<string>();
        }

        var eligibleMikanIdSet = eligibleMikanIds
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var subgroupRows = await _dbContext.MikanFeedItems
            .AsNoTracking()
            .Where(item => item.Subgroup != null)
            .Select(item => new
            {
                item.MikanBangumiId,
                Subgroup = item.Subgroup!
            })
            .ToListAsync();

        var options = subgroupRows
            .Where(row => eligibleMikanIdSet.Contains(row.MikanBangumiId))
            .Select(row => row.Subgroup.Trim())
            .Where(value => value.Length > 0)
            .Where(IsValidSubgroupOption)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _logger.LogDebug(
            "Loaded subgroup options from recent anime. EligibleMikanIds={EligibleCount}, Options={OptionsCount}",
            eligibleMikanIdSet.Count,
            options.Count);

        return options;
    }

    private static bool TryParseAirYear(string airDate, out int year)
    {
        year = 0;
        if (string.IsNullOrWhiteSpace(airDate) || airDate.Length < 4)
        {
            return false;
        }

        return int.TryParse(airDate[..4], out year);
    }

    private static bool IsValidSubgroupOption(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Length < 2 || trimmed.Length > 48)
        {
            return false;
        }

        if (SubgroupNoiseTokenRegex.IsMatch(trimmed) || SubgroupEpisodeHintRegex.IsMatch(trimmed))
        {
            return false;
        }

        var whitespaceCount = trimmed.Count(char.IsWhiteSpace);
        if (whitespaceCount > 3)
        {
            return false;
        }

        var allNumericPunctuation = trimmed.All(ch => char.IsDigit(ch) || ch is '.' or '_' or '-');
        return !allNumericPunctuation;
    }

    private string ReadPreferenceValue(string key, string fallback)
    {
        var configured = _configuration[key];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return fallback;
        }

        return configured.Trim();
    }

    private static void EnsureContainsIgnoreCase(List<string> options, string value, bool insertAtBeginning = false)
    {
        var existingIndex = options.FindIndex(option => string.Equals(option, value, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            if (insertAtBeginning && existingIndex != 0)
            {
                var existingValue = options[existingIndex];
                options.RemoveAt(existingIndex);
                options.Insert(0, existingValue);
            }
            return;
        }

        if (insertAtBeginning)
        {
            options.Insert(0, value);
        }
        else
        {
            options.Add(value);
        }
    }

    private List<string> ReadPriorityOrderFromConfiguration()
    {
        var configuredOrder = _configuration.GetSection("DownloadPreferences:PriorityOrder").Get<string[]>();
        if (configuredOrder is null || configuredOrder.Length == 0)
        {
            return DownloadPreferenceFields.ToList();
        }

        try
        {
            return ValidatePriorityOrder(configuredOrder);
        }
        catch
        {
            return DownloadPreferenceFields.ToList();
        }
    }

    private static List<string> ValidatePriorityOrder(IEnumerable<string>? order)
    {
        if (order is null)
        {
            return DownloadPreferenceFields.ToList();
        }

        var normalized = order
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .ToList();

        if (normalized.Count != DownloadPreferenceFields.Length)
        {
            throw new InvalidCredentialsException(
                "downloadPreferences.priorityOrder",
                "Priority order must include subgroup, resolution, and subtitleType");
        }

        var comparer = StringComparer.OrdinalIgnoreCase;
        var hasAllFields = DownloadPreferenceFields.All(field => normalized.Contains(field, comparer));
        var hasDuplicates = normalized
            .GroupBy(item => item, comparer)
            .Any(group => group.Count() > 1);

        if (!hasAllFields || hasDuplicates)
        {
            throw new InvalidCredentialsException(
                "downloadPreferences.priorityOrder",
                "Priority order must be a unique permutation of subgroup, resolution, subtitleType");
        }

        return normalized
            .Select(item => DownloadPreferenceFields.First(field => string.Equals(field, item, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private static string MaskToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return "****";
        }

        var trimmed = token.Trim();
        if (trimmed.Length < 12)
        {
            return "****";
        }

        return $"{trimmed[..4]}...{trimmed[^4..]}";
    }
}

public record UpdateTokensRequest(string? TmdbToken);

public record SettingsProfileResponse(
    SettingsTokenStatusDto Tmdb,
    QbittorrentSettingsDto Qbittorrent,
    AnimeSubSettingsDto AnimeSub,
    MikanSettingsDto Mikan,
    DownloadPreferencesSettingsDto DownloadPreferences,
    DownloadPreferenceOptionsDto DownloadPreferenceOptions);

public record SettingsTokenStatusDto(
    bool Configured,
    string? Preview);

public record QbittorrentSettingsDto(
    string Host,
    int Port,
    string Username,
    bool PasswordConfigured,
    string DefaultSavePath,
    string Category,
    string Tags,
    bool UseAnimeSubPath);

public record AnimeSubSettingsDto(
    string Username,
    bool PasswordConfigured);

public record MikanSettingsDto(
    int PollingIntervalMinutes);

public record DownloadPreferencesSettingsDto(
    string Subgroup,
    string Resolution,
    string SubtitleType,
    IReadOnlyList<string> PriorityOrder);

public record DownloadPreferenceOptionsDto(
    IReadOnlyList<string> Subgroups,
    IReadOnlyList<string> Resolutions,
    IReadOnlyList<string> SubtitleTypes,
    IReadOnlyList<string> PriorityFields);

public record UpdateSettingsProfileRequest(
    string? TmdbToken,
    UpdateQbittorrentSettingsRequest? Qbittorrent,
    UpdateAnimeSubSettingsRequest? AnimeSub,
    UpdateMikanSettingsRequest? Mikan,
    UpdateDownloadPreferencesRequest? DownloadPreferences);

public record UpdateQbittorrentSettingsRequest(
    string? Host,
    int? Port,
    string? Username,
    string? Password,
    string? DefaultSavePath,
    string? Category,
    string? Tags,
    bool? UseAnimeSubPath);

public record UpdateAnimeSubSettingsRequest(
    string? Username,
    string? Password);

public record UpdateMikanSettingsRequest(
    int? PollingIntervalMinutes);

public record UpdateDownloadPreferencesRequest(
    string? Subgroup,
    string? Resolution,
    string? SubtitleType,
    IReadOnlyList<string>? PriorityOrder);

public record TestTmdbTokenRequest(string? TmdbToken);

public record TestQbittorrentRequest(
    string? Field,
    string? Host,
    int? Port,
    string? Username,
    string? Password,
    string? DefaultSavePath,
    string? Category,
    string? Tags);

public record TestMikanPollingRequest(int? PollingIntervalMinutes);

public record SettingsTestResponse(bool Success, string Message);
