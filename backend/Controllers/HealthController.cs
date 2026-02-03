using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

/// <summary>
/// Health check endpoints for monitoring and readiness probes
/// </summary>
[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HealthController> _logger;

    public HealthController(
        IHttpClientFactory httpClientFactory,
        ILogger<HealthController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Basic health check endpoint
    /// GET /health
    /// </summary>
    /// <returns>Health status</returns>
    [HttpGet]
    public IActionResult GetHealth()
    {
        return Ok(new
        {
            status = "healthy",
            timestamp = DateTime.UtcNow,
            version = "1.0.0"
        });
    }

    /// <summary>
    /// Check external API dependencies (Bangumi, TMDB, AniList)
    /// GET /health/dependencies
    /// </summary>
    /// <returns>Dependency health status</returns>
    [HttpGet("dependencies")]
    public async Task<IActionResult> CheckDependencies()
    {
        var checks = new Dictionary<string, object>();
        var allHealthy = true;

        // Check Bangumi API (required)
        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(5);
            var response = await httpClient.GetAsync("https://api.bgm.tv/calendar");

            checks["bangumi"] = new
            {
                status = response.IsSuccessStatusCode ? "healthy" : "degraded",
                statusCode = (int)response.StatusCode,
                required = true
            };

            if (!response.IsSuccessStatusCode)
            {
                allHealthy = false;
                _logger.LogWarning("Bangumi API health check failed with status {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bangumi API health check failed");
            checks["bangumi"] = new
            {
                status = "unhealthy",
                error = ex.Message,
                required = true
            };
            allHealthy = false;
        }

        // Check TMDB API (optional)
        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(5);
            var response = await httpClient.GetAsync("https://api.themoviedb.org/3");

            checks["tmdb"] = new
            {
                status = response.IsSuccessStatusCode ? "healthy" : "degraded",
                statusCode = (int)response.StatusCode,
                required = false
            };

            // TMDB is optional, don't mark overall health as unhealthy
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogInformation("TMDB API health check degraded with status {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "TMDB API health check failed (optional service)");
            checks["tmdb"] = new
            {
                status = "unavailable",
                error = ex.Message,
                required = false
            };
        }

        // Check AniList API (optional)
        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(5);
            var response = await httpClient.GetAsync("https://graphql.anilist.co");

            checks["anilist"] = new
            {
                status = response.IsSuccessStatusCode ? "healthy" : "degraded",
                statusCode = (int)response.StatusCode,
                required = false
            };

            // AniList is optional, don't mark overall health as unhealthy
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogInformation("AniList API health check degraded with status {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "AniList API health check failed (optional service)");
            checks["anilist"] = new
            {
                status = "unavailable",
                error = ex.Message,
                required = false
            };
        }

        var result = new
        {
            status = allHealthy ? "healthy" : "degraded",
            timestamp = DateTime.UtcNow,
            checks
        };

        return allHealthy ? Ok(result) : StatusCode(503, result);
    }
}
