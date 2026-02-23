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
    /// Validate TMDB token (optional, API Read Access Token)
    /// According to TMDB API documentation, API Read Access Tokens are typically 100+ characters (JWT-like)
    /// </summary>
    public void ValidateTmdbToken(string? token)
    {
        // TMDB token is optional, skip validation if not provided
        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogInformation("TMDB token not provided (optional, English metadata will be unavailable)");
            return;
        }

        // TMDB API Read Access Tokens are typically 100+ characters (JWT format)
        if (token.Length < 100)
        {
            _logger.LogWarning("TMDB token validation failed: token too short (length: {Length})", token.Length);
            throw new InvalidCredentialsException(
                "TMDBToken",
                "TMDB token appears invalid (too short). Expected API Read Access Token (JWT format).");
        }

        _logger.LogDebug("TMDB token validated successfully");
    }
}
