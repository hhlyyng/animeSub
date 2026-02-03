using Microsoft.AspNetCore.Mvc;
using backend.Services;
using backend.Services.Validators;
using backend.Services.Exceptions;

namespace backend.Controllers;

/// <summary>
/// API controller for managing application settings (tokens, preferences)
/// </summary>
[ApiController]
[Route("api/settings")]
public class SettingsController : ControllerBase
{
    private readonly ITokenStorageService _tokenStorage;
    private readonly TokenValidator _tokenValidator;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(
        ITokenStorageService tokenStorage,
        TokenValidator tokenValidator,
        ILogger<SettingsController> logger)
    {
        _tokenStorage = tokenStorage;
        _tokenValidator = tokenValidator;
        _logger = logger;
    }

    /// <summary>
    /// Get current token configuration status
    /// Returns whether tokens are configured (not the actual tokens)
    /// GET /api/settings/tokens
    /// </summary>
    [HttpGet("tokens")]
    public async Task<IActionResult> GetTokenStatus()
    {
        try
        {
            var bangumiToken = await _tokenStorage.GetBangumiTokenAsync();
            var tmdbToken = await _tokenStorage.GetTmdbTokenAsync();

            var hasBangumi = !string.IsNullOrWhiteSpace(bangumiToken);
            var hasTmdb = !string.IsNullOrWhiteSpace(tmdbToken);

            return Ok(new
            {
                bangumi = new
                {
                    configured = hasBangumi,
                    preview = hasBangumi ? MaskToken(bangumiToken) : null
                },
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
    /// Update API tokens
    /// PUT /api/settings/tokens
    /// Body: { "bangumiToken": "xxx", "tmdbToken": "yyy" }
    /// </summary>
    [HttpPut("tokens")]
    public async Task<IActionResult> UpdateTokens([FromBody] UpdateTokensRequest request)
    {
        try
        {
            // Validate token formats before saving
            if (!string.IsNullOrWhiteSpace(request.BangumiToken))
            {
                _tokenValidator.ValidateBangumiToken(request.BangumiToken);
            }

            if (!string.IsNullOrWhiteSpace(request.TmdbToken))
            {
                _tokenValidator.ValidateTmdbToken(request.TmdbToken);
            }

            // Save encrypted tokens
            await _tokenStorage.SaveTokensAsync(request.BangumiToken, request.TmdbToken);

            _logger.LogInformation("API tokens updated successfully");
            return Ok(new
            {
                message = "Tokens updated successfully",
                bangumi = new { configured = !string.IsNullOrWhiteSpace(request.BangumiToken) },
                tmdb = new { configured = !string.IsNullOrWhiteSpace(request.TmdbToken) }
            });
        }
        catch (InvalidCredentialsException ex)
        {
            _logger.LogWarning("Token validation failed: {Message}", ex.Message);
            return BadRequest(new
            {
                error = ex.Message,
                field = ex.CredentialType
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update tokens");
            return StatusCode(500, new { error = "Failed to save tokens" });
        }
    }

    /// <summary>
    /// Delete all stored tokens
    /// DELETE /api/settings/tokens
    /// </summary>
    [HttpDelete("tokens")]
    public async Task<IActionResult> DeleteTokens()
    {
        try
        {
            await _tokenStorage.SaveTokensAsync(null, null);
            _logger.LogInformation("All tokens deleted");
            return Ok(new { message = "All tokens deleted successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete tokens");
            return StatusCode(500, new { error = "Failed to delete tokens" });
        }
    }

    /// <summary>
    /// Mask token for preview (show first 4 and last 4 characters)
    /// </summary>
    private static string? MaskToken(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length < 12)
            return "****";

        return $"{token[..4]}...{token[^4..]}";
    }
}

/// <summary>
/// Request model for updating tokens
/// </summary>
public record UpdateTokensRequest(string? BangumiToken, string? TmdbToken);
