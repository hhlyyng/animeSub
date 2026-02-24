using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using backend.Data.Entities;
using backend.Models.Dtos;
using backend.Services;
using backend.Services.Interfaces;
using backend.Services.Repositories;
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
    private readonly IAnimeRepository _animeRepository;
    private readonly IAnimePoolService _animePoolService;
    private readonly ILogger<AnimeController> _logger;

    public AnimeController(
        IAnimeAggregationService aggregationService,
        ITokenStorageService tokenStorage,
        TokenValidator tokenValidator,
        IAnimeRepository animeRepository,
        IAnimePoolService animePoolService,
        ILogger<AnimeController> logger)
    {
        _aggregationService = aggregationService ?? throw new ArgumentNullException(nameof(aggregationService));
        _tokenStorage = tokenStorage ?? throw new ArgumentNullException(nameof(tokenStorage));
        _tokenValidator = tokenValidator ?? throw new ArgumentNullException(nameof(tokenValidator));
        _animeRepository = animeRepository ?? throw new ArgumentNullException(nameof(animeRepository));
        _animePoolService = animePoolService ?? throw new ArgumentNullException(nameof(animePoolService));
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
    /// Search anime by title keyword
    /// </summary>
    [HttpGet("search")]
    [ProducesResponseType(typeof(ApiResponseDto<AnimeListDataDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchAnime([FromQuery] string q, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest(new { success = false, message = "Query parameter 'q' is required" });

        _logger.LogInformation("Received search request for: {Query}", q);

        var bangumiToken = await _tokenStorage.GetBangumiTokenAsync()
            ?? Request.Headers["X-Bangumi-Token"].FirstOrDefault();
        var tmdbToken = await _tokenStorage.GetTmdbTokenAsync()
            ?? Request.Headers["X-TMDB-Token"].FirstOrDefault();

        var response = await _aggregationService.SearchAnimeAsync(q, bangumiToken, tmdbToken, cancellationToken);

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

    /// <summary>
    /// Get random anime picks from the pre-built recommendation pool
    /// </summary>
    /// <remarks>
    /// Returns HTTP 202 with building=true if the pool is still being built on first startup.
    /// The pool is rebuilt every 24 hours in the background.
    /// </remarks>
    /// <param name="count">Number of random picks to return (1-20, default 10)</param>
    [HttpGet("random")]
    public async Task<IActionResult> GetRandomAnime(
        [FromQuery] int count = 10,
        CancellationToken cancellationToken = default)
    {
        count = Math.Clamp(count, 1, 20);

        // Always call GetRandomPicksAsync — it has a SQLite L2 fallback and will warm the
        // memory cache on first hit after a restart, even before the builder runs.
        var picks = await _animePoolService.GetRandomPicksAsync(count, cancellationToken);
        if (picks.Count == 0)
        {
            return StatusCode(202, new
            {
                success = false,
                building = _animePoolService.IsBuilding,
                message = "推荐库正在构建中，请稍后重试"
            });
        }

        return Ok(new
        {
            success = true,
            data = new { count = picks.Count, animes = picks, poolSize = _animePoolService.PoolSize },
            message = $"随机推荐 {picks.Count} 部动画"
        });
    }

    /// <summary>
    /// Batch lookup anime info by Bangumi IDs from the local database
    /// </summary>
    [HttpPost("batch")]
    public async Task<IActionResult> GetAnimeBatch([FromBody] List<int> bangumiIds)
    {
        if (bangumiIds == null || bangumiIds.Count == 0)
        {
            return Ok(new { success = true, data = new { animes = Array.Empty<AnimeInfoDto>() } });
        }

        var validIds = bangumiIds.Where(id => id > 0).Distinct().Take(50).ToList();
        if (validIds.Count == 0)
        {
            return Ok(new { success = true, data = new { animes = Array.Empty<AnimeInfoDto>() } });
        }

        var entities = await _animeRepository.GetAnimeInfoBatchAsync(validIds);
        var dtos = entities.Select(ConvertEntityToDto).ToList();

        return Ok(new
        {
            success = true,
            data = new { animes = dtos }
        });
    }

    private static AnimeInfoDto ConvertEntityToDto(AnimeInfoEntity entity)
    {
        return new AnimeInfoDto
        {
            BangumiId = entity.BangumiId.ToString(),
            JpTitle = entity.NameJapanese ?? "",
            ChTitle = entity.NameChinese ?? "",
            EnTitle = entity.NameEnglish ?? "",
            ChDesc = entity.DescChinese ?? "",
            EnDesc = entity.DescEnglish ?? "",
            Score = entity.Score ?? "0",
            Images = new AnimeImagesDto
            {
                Portrait = entity.ImagePortrait ?? "",
                Landscape = entity.ImageLandscape ?? ""
            },
            ExternalUrls = new ExternalUrlsDto
            {
                Bangumi = entity.UrlBangumi ?? $"https://bgm.tv/subject/{entity.BangumiId}",
                Tmdb = entity.UrlTmdb ?? "",
                Anilist = entity.UrlAnilist ?? ""
            },
            MikanBangumiId = entity.MikanBangumiId
        };
    }
}
