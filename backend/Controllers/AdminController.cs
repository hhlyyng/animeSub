using Microsoft.AspNetCore.Mvc;
using backend.Models;
using backend.Services.Background;
using backend.Services.Repositories;

namespace backend.Controllers;

/// <summary>
/// Admin endpoints for system management and maintenance
/// </summary>
[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IAnimeRepository _repository;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        IServiceProvider serviceProvider,
        IAnimeRepository repository,
        ILogger<AdminController> logger)
    {
        _serviceProvider = serviceProvider;
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Get the current status of the pre-fetch service
    /// GET /api/admin/prefetch/status
    /// </summary>
    /// <returns>Pre-fetch service status</returns>
    [HttpGet("prefetch/status")]
    public IActionResult GetPreFetchStatus()
    {
        var status = AnimePreFetchService.GetStatus();
        return Ok(status);
    }

    /// <summary>
    /// Manually trigger a pre-fetch operation
    /// POST /api/admin/prefetch
    /// </summary>
    /// <returns>Trigger result</returns>
    [HttpPost("prefetch")]
    public async Task<IActionResult> TriggerPreFetch()
    {
        var status = AnimePreFetchService.GetStatus();

        if (status.IsRunning)
        {
            return Conflict(new
            {
                success = false,
                message = "Pre-fetch is already running",
                startedAt = status.LastRunTime
            });
        }

        _logger.LogInformation("Manual pre-fetch triggered via API");

        // Run pre-fetch in background
        var backgroundService = _serviceProvider.GetServices<IHostedService>()
            .OfType<AnimePreFetchService>()
            .FirstOrDefault();

        if (backgroundService == null)
        {
            return StatusCode(503, new
            {
                success = false,
                message = "Pre-fetch service is not available"
            });
        }

        // Start the pre-fetch in a background task
        _ = Task.Run(async () =>
        {
            try
            {
                await backgroundService.RunPreFetchAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Manual pre-fetch failed");
            }
        });

        return Accepted(new
        {
            success = true,
            message = "Pre-fetch started",
            checkStatusAt = "/api/admin/prefetch/status"
        });
    }

    /// <summary>
    /// Get database statistics for pre-fetched anime
    /// GET /api/admin/prefetch/stats
    /// </summary>
    /// <returns>Database statistics</returns>
    [HttpGet("prefetch/stats")]
    public async Task<IActionResult> GetPreFetchStats()
    {
        try
        {
            var stats = new Dictionary<string, object>();

            // Get count per weekday
            for (int weekday = 1; weekday <= 7; weekday++)
            {
                var animes = await _repository.GetAnimesByWeekdayAsync(weekday);
                stats[$"weekday_{weekday}"] = new
                {
                    count = animes.Count,
                    preFetched = animes.Count(a => a.IsPreFetched),
                    realTime = animes.Count(a => !a.IsPreFetched)
                };
            }

            // Get overall stats
            int totalCount = 0;
            int totalPreFetched = 0;
            for (int w = 1; w <= 7; w++)
            {
                var animes = await _repository.GetAnimesByWeekdayAsync(w);
                totalCount += animes.Count;
                totalPreFetched += animes.Count(a => a.IsPreFetched);
            }

            return Ok(new
            {
                total = totalCount,
                preFetched = totalPreFetched,
                realTime = totalCount - totalPreFetched,
                byWeekday = stats
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get pre-fetch stats");
            return StatusCode(500, new
            {
                success = false,
                message = "Failed to retrieve statistics",
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// Clear all pre-fetched anime data from the database
    /// DELETE /api/admin/prefetch/data
    /// </summary>
    /// <returns>Deletion result</returns>
    [HttpDelete("prefetch/data")]
    public async Task<IActionResult> ClearPreFetchedData()
    {
        var status = AnimePreFetchService.GetStatus();

        if (status.IsRunning)
        {
            return Conflict(new
            {
                success = false,
                message = "Cannot clear data while pre-fetch is running"
            });
        }

        try
        {
            _logger.LogWarning("Clearing all pre-fetched anime data via API");

            // Clear anime data from repository
            await _repository.ClearAllAnimeDataAsync();

            return Ok(new
            {
                success = true,
                message = "All pre-fetched anime data has been cleared"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear pre-fetch data");
            return StatusCode(500, new
            {
                success = false,
                message = "Failed to clear data",
                error = ex.Message
            });
        }
    }
}
