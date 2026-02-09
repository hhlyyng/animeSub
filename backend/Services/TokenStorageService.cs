using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace backend.Services;

/// <summary>
/// Interface for managing persistent storage of API tokens
/// </summary>
public interface ITokenStorageService
{
    Task<string?> GetBangumiTokenAsync();
    Task<string?> GetTmdbTokenAsync();
    Task SaveTokensAsync(string? bangumiToken, string? tmdbToken);
    Task<bool> HasBangumiTokenAsync();
}

/// <summary>
/// Resolves API tokens with appsettings-first strategy and legacy file fallback.
/// Legacy file storage still uses Data Protection encryption.
/// </summary>
public class TokenStorageService : ITokenStorageService
{
    private readonly IConfiguration _configuration;
    private readonly string _filePath;
    private readonly IDataProtector _protector;
    private readonly ILogger<TokenStorageService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public TokenStorageService(
        IWebHostEnvironment env,
        IConfiguration configuration,
        IDataProtectionProvider protectionProvider,
        ILogger<TokenStorageService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _filePath = Path.Combine(env.ContentRootPath, "appsettings.user.json");

        // Create dedicated protector with specific purpose string
        _protector = protectionProvider.CreateProtector("AnimeSubscription.TokenStorage.v1");

        _logger.LogInformation(
            "Token storage initialized. Primary source: appsettings.json, legacy source: {Path}",
            _filePath);
    }

    public async Task<string?> GetBangumiTokenAsync()
    {
        // Development priority: appsettings.json
        var configuredToken = _configuration["ApiTokens:BangumiToken"]
            ?? _configuration["PreFetch:BangumiToken"];
        if (!string.IsNullOrWhiteSpace(configuredToken))
        {
            return configuredToken;
        }

        // Legacy fallback
        var tokens = await ReadTokensAsync();
        return DecryptToken(tokens?.BangumiToken);
    }

    public async Task<string?> GetTmdbTokenAsync()
    {
        // Development priority: appsettings.json
        var configuredToken = _configuration["ApiTokens:TmdbToken"]
            ?? _configuration["PreFetch:TmdbToken"];
        if (!string.IsNullOrWhiteSpace(configuredToken))
        {
            return configuredToken;
        }

        // Legacy fallback
        var tokens = await ReadTokensAsync();
        return DecryptToken(tokens?.TmdbToken);
    }

    public async Task<bool> HasBangumiTokenAsync()
    {
        var token = await GetBangumiTokenAsync();
        return !string.IsNullOrWhiteSpace(token);
    }

    public async Task SaveTokensAsync(string? bangumiToken, string? tmdbToken)
    {
        var configuredBangumiToken = _configuration["ApiTokens:BangumiToken"]
            ?? _configuration["PreFetch:BangumiToken"];
        var configuredTmdbToken = _configuration["ApiTokens:TmdbToken"]
            ?? _configuration["PreFetch:TmdbToken"];
        if (!string.IsNullOrWhiteSpace(configuredBangumiToken) || !string.IsNullOrWhiteSpace(configuredTmdbToken))
        {
            _logger.LogInformation(
                "appsettings tokens are configured; legacy saved tokens are fallback only unless appsettings tokens are cleared");
        }

        await _lock.WaitAsync();
        try
        {
            var tokens = new UserTokens
            {
                BangumiToken = EncryptToken(bangumiToken),
                TmdbToken = EncryptToken(tmdbToken),
                UpdatedAt = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(tokens, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(_filePath, json);
            _logger.LogInformation("Tokens saved and encrypted successfully");
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<UserTokens?> ReadTokensAsync()
    {
        if (!File.Exists(_filePath))
        {
            _logger.LogDebug("Legacy token file not found: {Path}", _filePath);
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_filePath);
            return JsonSerializer.Deserialize<UserTokens>(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read token file");
            return null;
        }
    }

    /// <summary>
    /// Encrypt token using Data Protection API
    /// </summary>
    private string? EncryptToken(string? plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
            return null;

        try
        {
            return _protector.Protect(plainText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to encrypt token");
            throw;
        }
    }

    /// <summary>
    /// Decrypt token using Data Protection API
    /// </summary>
    private string? DecryptToken(string? encryptedText)
    {
        if (string.IsNullOrWhiteSpace(encryptedText))
            return null;

        try
        {
            return _protector.Unprotect(encryptedText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt token (may be corrupted or key changed)");
            return null;
        }
    }

    private class UserTokens
    {
        public string? BangumiToken { get; set; }
        public string? TmdbToken { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
