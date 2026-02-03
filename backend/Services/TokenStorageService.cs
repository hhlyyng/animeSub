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
/// Manages encrypted persistent storage of API tokens in JSON file
/// Uses ASP.NET Core Data Protection API for encryption
/// </summary>
public class TokenStorageService : ITokenStorageService
{
    private readonly string _filePath;
    private readonly IDataProtector _protector;
    private readonly ILogger<TokenStorageService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public TokenStorageService(
        IWebHostEnvironment env,
        IDataProtectionProvider protectionProvider,
        ILogger<TokenStorageService> logger)
    {
        _logger = logger;
        _filePath = Path.Combine(env.ContentRootPath, "appsettings.user.json");

        // Create dedicated protector with specific purpose string
        _protector = protectionProvider.CreateProtector("AnimeSubscription.TokenStorage.v1");

        _logger.LogInformation("Token storage initialized at {Path}", _filePath);
    }

    public async Task<string?> GetBangumiTokenAsync()
    {
        var tokens = await ReadTokensAsync();
        return DecryptToken(tokens?.BangumiToken);
    }

    public async Task<string?> GetTmdbTokenAsync()
    {
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
            _logger.LogWarning("Token file not found: {Path}", _filePath);
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
