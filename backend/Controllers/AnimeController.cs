using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using backend.Models.Dtos;
using backend.Services;
using backend.Services.Interfaces;
using backend.Services.Validators;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
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

        // Get TMDB token from storage (priority) or headers (fallback for testing)
        var tmdbToken = await _tokenStorage.GetTmdbTokenAsync()
            ?? Request.Headers["X-TMDB-Token"].FirstOrDefault();

        // Validate token (throws InvalidCredentialsException if invalid)
        _tokenValidator.ValidateTmdbToken(tmdbToken);

        // Get aggregated anime data with data source tracking
        // All exceptions are handled by global exception middleware
        var response = await _aggregationService.GetTodayAnimeEnrichedAsync(
            tmdbToken,
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
    /// <remarks>
    /// TMDB token can be provided via header for backdrop images.
    /// </remarks>
    [HttpGet("top/bangumi")]
    [ProducesResponseType(typeof(ApiResponseDto<AnimeListDataDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTopBangumi(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Received request for Bangumi Top 10");

        // Get TMDB token for backdrop enrichment
        var tmdbToken = await _tokenStorage.GetTmdbTokenAsync()
            ?? Request.Headers["X-TMDB-Token"].FirstOrDefault();

        var response = await _aggregationService.GetTopAnimeFromBangumiAsync(
            tmdbToken, 10, cancellationToken);

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
    /// <remarks>
    /// TMDB token can be provided via header for better backdrop images.
    /// </remarks>
    [HttpGet("top/anilist")]
    [ProducesResponseType(typeof(ApiResponseDto<AnimeListDataDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTopAniList(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Received request for AniList Top 10");

        // Get TMDB token for backdrop enrichment
        var tmdbToken = await _tokenStorage.GetTmdbTokenAsync()
            ?? Request.Headers["X-TMDB-Token"].FirstOrDefault();

        var response = await _aggregationService.GetTopAnimeFromAniListAsync(tmdbToken, 10, cancellationToken);

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
    /// <remarks>
    /// TMDB token can be provided via header for backdrop images.
    /// </remarks>
    [HttpGet("top/mal")]
    [ProducesResponseType(typeof(ApiResponseDto<AnimeListDataDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTopMAL(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Received request for MAL Top 10");

        // Get TMDB token for backdrop enrichment
        var tmdbToken = await _tokenStorage.GetTmdbTokenAsync()
            ?? Request.Headers["X-TMDB-Token"].FirstOrDefault();

        var response = await _aggregationService.GetTopAnimeFromMALAsync(tmdbToken, 10, cancellationToken);

        return Ok(new
        {
            success = response.Success,
            data = new { count = response.Count, animes = response.Animes },
            message = response.Message
        });
    }
}
