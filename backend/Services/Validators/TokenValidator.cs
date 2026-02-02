using backend.Services.Exceptions;

namespace backend.Services.Validators;

/// <summary>
/// Validator for API tokens
/// </summary>
public class TokenValidator
{
    private readonly ILogger<TokenValidator> _logger;

    public TokenValidator(ILogger<TokenValidator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Validate Bangumi token (required)
    /// </summary>
    public void ValidateBangumiToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning("Bangumi token validation failed: token is missing");
            throw new InvalidCredentialsException(
                "BangumiToken",
                "Bangumi token is required. Please provide X-Bangumi-Token header.");
        }

        // Additional validation rules can be added here
        // e.g., token format, length, pattern matching
        if (token.Length < 10)
        {
            _logger.LogWarning("Bangumi token validation failed: token too short");
            throw new InvalidCredentialsException(
                "BangumiToken",
                "Bangumi token appears to be invalid (too short).");
        }
    }

    /// <summary>
    /// Validate TMDB token (optional)
    /// </summary>
    public void ValidateTmdbToken(string? token)
    {
        // TMDB token is optional, so only validate if provided
        if (!string.IsNullOrWhiteSpace(token) && token.Length < 10)
        {
            _logger.LogWarning("TMDB token validation failed: token too short");
            throw new InvalidCredentialsException(
                "TMDBToken",
                "TMDB token appears to be invalid (too short).");
        }
    }

    /// <summary>
    /// Validate all request tokens
    /// </summary>
    public (string bangumiToken, string? tmdbToken) ValidateRequestTokens(
        string? bangumiToken,
        string? tmdbToken)
    {
        ValidateBangumiToken(bangumiToken);
        ValidateTmdbToken(tmdbToken);

        return (bangumiToken!, tmdbToken);
    }
}
