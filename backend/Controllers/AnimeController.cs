using Microsoft.AspNetCore.Mvc;
using backend.Models.Dtos;
using backend.Services;
using backend.Services.Interfaces;
using backend.Services.Validators;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AnimeController : ControllerBase
{
    private readonly IAnimeAggregationService _aggregationService;
    private readonly ITokenStorageService _tokenStorage;
    private readonly TokenValidator _tokenValidator;
    private readonly ILogger<AnimeController> _logger;

    public AnimeController(
        IAnimeAggregationService aggregationService,
        ITokenStorageService tokenStorage,
        TokenValidator tokenValidator,
        ILogger<AnimeController> logger)
    {
        _aggregationService = aggregationService ?? throw new ArgumentNullException(nameof(aggregationService));
        _tokenStorage = tokenStorage ?? throw new ArgumentNullException(nameof(tokenStorage));
        _tokenValidator = tokenValidator ?? throw new ArgumentNullException(nameof(tokenValidator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Get today's anime broadcast schedule with enriched data from multiple sources
    /// </summary>
    /// <remarks>
    /// Tokens can be configured in settings (recommended) or provided via headers.
    /// Priority: Stored configuration > HTTP headers
    ///
    /// Sample request:
    ///
    ///     GET /api/anime/today
    ///     X-Bangumi-Token: your_bangumi_token (optional if configured)
    ///     X-TMDB-Token: your_tmdb_token (optional)
    ///
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of today's anime with aggregated metadata</returns>
    /// <response code="200">Returns today's anime list</response>
    /// <response code="400">Invalid request (missing or invalid token)</response>
    /// <response code="401">Unauthorized (invalid credentials)</response>
    /// <response code="408">Request timeout</response>
    /// <response code="502">External API error</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("today")]
    [ProducesResponseType(typeof(ApiResponseDto<AnimeListDataDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Models.ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Models.ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Models.ErrorResponse), StatusCodes.Status408RequestTimeout)]
    [ProducesResponseType(typeof(Models.ErrorResponse), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(Models.ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetTodayAnime(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Received request for today's anime schedule");

        // Get tokens from storage (priority) or headers (fallback for testing)
        var bangumiToken = await _tokenStorage.GetBangumiTokenAsync()
            ?? Request.Headers["X-Bangumi-Token"].FirstOrDefault();
        var tmdbToken = await _tokenStorage.GetTmdbTokenAsync()
            ?? Request.Headers["X-TMDB-Token"].FirstOrDefault();

        // Validate tokens (throws InvalidCredentialsException if invalid)
        var (validatedBangumiToken, validatedTmdbToken) = _tokenValidator.ValidateRequestTokens(
            bangumiToken,
            tmdbToken);

        // Get aggregated anime data with data source tracking
        // All exceptions are handled by global exception middleware
        var response = await _aggregationService.GetTodayAnimeEnrichedAsync(
            validatedBangumiToken,
            validatedTmdbToken,
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Retrieved {Count} anime for today (source: {DataSource}, stale: {IsStale}, retries: {RetryAttempts})",
            response.Count, response.DataSource, response.IsStale, response.RetryAttempts);

        // Return response with data source metadata for frontend
        return Ok(new
        {
            success = response.Success,
            data = new
            {
                count = response.Count,
                animes = response.Animes
            },
            metadata = new
            {
                dataSource = response.DataSource.ToString().ToLowerInvariant(),  // "api", "cache", "cachefallback"
                isStale = response.IsStale,
                lastUpdated = response.LastUpdated?.ToString("o"),  // ISO 8601 format
                retryAttempts = response.RetryAttempts
            },
            message = response.Message
        });
    }

    /// <summary>
    /// Get top 10 anime from Bangumi rankings
    /// </summary>
    [HttpGet("top/bangumi")]
    [ProducesResponseType(typeof(ApiResponseDto<AnimeListDataDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTopBangumi(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Received request for Bangumi Top 10");

        var bangumiToken = await _tokenStorage.GetBangumiTokenAsync()
            ?? Request.Headers["X-Bangumi-Token"].FirstOrDefault();

        var (validatedBangumiToken, _) = _tokenValidator.ValidateRequestTokens(bangumiToken, null);

        var response = await _aggregationService.GetTopAnimeFromBangumiAsync(
            validatedBangumiToken, 10, cancellationToken);

        return Ok(new
        {
            success = response.Success,
            data = new { count = response.Count, animes = response.Animes },
            message = response.Message
        });
    }

    /// <summary>
    /// Get top 10 trending anime from AniList
    /// </summary>
    [HttpGet("top/anilist")]
    [ProducesResponseType(typeof(ApiResponseDto<AnimeListDataDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTopAniList(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Received request for AniList Top 10");

        var response = await _aggregationService.GetTopAnimeFromAniListAsync(10, cancellationToken);

        return Ok(new
        {
            success = response.Success,
            data = new { count = response.Count, animes = response.Animes },
            message = response.Message
        });
    }

    /// <summary>
    /// Get top 10 anime from MyAnimeList (via Jikan API)
    /// </summary>
    [HttpGet("top/mal")]
    [ProducesResponseType(typeof(ApiResponseDto<AnimeListDataDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTopMAL(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Received request for MAL Top 10");

        var response = await _aggregationService.GetTopAnimeFromMALAsync(10, cancellationToken);

        return Ok(new
        {
            success = response.Success,
            data = new { count = response.Count, animes = response.Animes },
            message = response.Message
        });
    }
}