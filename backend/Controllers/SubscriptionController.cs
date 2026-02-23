using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using backend.Models.Dtos;
using backend.Services.Interfaces;

namespace backend.Controllers;

/// <summary>
/// API controller for managing anime subscriptions
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SubscriptionController : ControllerBase
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly ILogger<SubscriptionController> _logger;

    public SubscriptionController(
        ISubscriptionService subscriptionService,
        ILogger<SubscriptionController> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    /// <summary>
    /// Get all subscriptions
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<SubscriptionResponse>>> GetAll()
    {
        var subscriptions = await _subscriptionService.GetAllSubscriptionsAsync();
        return Ok(subscriptions);
    }

    /// <summary>
    /// Get a subscription by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<SubscriptionResponse>> GetById(int id)
    {
        var subscription = await _subscriptionService.GetSubscriptionByIdAsync(id);
        if (subscription == null)
        {
            return NotFound(new { message = $"Subscription {id} not found" });
        }
        return Ok(subscription);
    }

    /// <summary>
    /// Get subscription by Bangumi ID
    /// </summary>
    [HttpGet("bangumi/{bangumiId:int}")]
    public async Task<ActionResult<SubscriptionResponse>> GetByBangumiId([FromRoute] int bangumiId)
    {
        if (bangumiId <= 0)
        {
            return BadRequest(new { message = "Valid BangumiId is required" });
        }

        var subscription = await _subscriptionService.GetSubscriptionByBangumiIdAsync(bangumiId);
        if (subscription == null)
        {
            return NotFound(new { message = $"Subscription for BangumiId {bangumiId} not found" });
        }

        return Ok(subscription);
    }

    /// <summary>
    /// Create a new subscription
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<SubscriptionResponse>> Create([FromBody] CreateSubscriptionRequest request)
    {
        if (string.IsNullOrEmpty(request.Title))
        {
            return BadRequest(new { message = "Title is required" });
        }

        if (string.IsNullOrEmpty(request.MikanBangumiId))
        {
            return BadRequest(new { message = "MikanBangumiId is required" });
        }

        if (request.BangumiId <= 0)
        {
            return BadRequest(new { message = "Valid BangumiId is required" });
        }

        try
        {
            var subscription = await _subscriptionService.CreateSubscriptionAsync(request);
            return CreatedAtAction(nameof(GetById), new { id = subscription.Id }, subscription);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Update a subscription
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<SubscriptionResponse>> Update(int id, [FromBody] UpdateSubscriptionRequest request)
    {
        var subscription = await _subscriptionService.UpdateSubscriptionAsync(id, request);
        if (subscription == null)
        {
            return NotFound(new { message = $"Subscription {id} not found" });
        }
        return Ok(subscription);
    }

    /// <summary>
    /// Delete a subscription
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(int id)
    {
        var deleted = await _subscriptionService.DeleteSubscriptionAsync(id);
        if (!deleted)
        {
            return NotFound(new { message = $"Subscription {id} not found" });
        }
        return NoContent();
    }

    /// <summary>
    /// Toggle subscription enabled status
    /// </summary>
    [HttpPost("{id}/toggle")]
    public async Task<ActionResult<SubscriptionResponse>> Toggle(int id, [FromQuery] bool enabled)
    {
        var subscription = await _subscriptionService.ToggleSubscriptionAsync(id, enabled);
        if (subscription == null)
        {
            return NotFound(new { message = $"Subscription {id} not found" });
        }
        return Ok(subscription);
    }

    /// <summary>
    /// Manually check a subscription for new torrents
    /// </summary>
    [HttpPost("{id}/check")]
    public async Task<ActionResult<CheckSubscriptionResponse>> Check(int id)
    {
        var result = await _subscriptionService.CheckSubscriptionAsync(id);
        if (!result.Success && result.ErrorMessage == "Subscription not found")
        {
            return NotFound(new { message = $"Subscription {id} not found" });
        }
        return Ok(result);
    }

    /// <summary>
    /// Check all enabled subscriptions for new torrents
    /// </summary>
    [HttpPost("check-all")]
    public async Task<ActionResult<CheckAllSubscriptionsResponse>> CheckAll()
    {
        _logger.LogInformation("Manual check-all triggered");
        var result = await _subscriptionService.CheckAllSubscriptionsAsync();
        return Ok(result);
    }

    /// <summary>
    /// Get lightweight task hashes for a subscription (for qBittorrent correlation)
    /// </summary>
    [HttpGet("{id}/task-hashes")]
    public async Task<ActionResult<List<TaskHashResponse>>> GetTaskHashes(int id, [FromQuery] int limit = 300)
    {
        var subscription = await _subscriptionService.GetSubscriptionByIdAsync(id);
        if (subscription == null)
        {
            return NotFound(new { message = $"Subscription {id} not found" });
        }

        var hashes = await _subscriptionService.GetTaskHashesAsync(id, limit);
        return Ok(hashes);
    }

    /// <summary>
    /// Get lightweight task hashes for manual downloads by Bangumi ID
    /// </summary>
    [HttpGet("manual-anime/{bangumiId:int}/task-hashes")]
    public async Task<ActionResult<List<TaskHashResponse>>> GetManualAnimeTaskHashes(
        [FromRoute] int bangumiId,
        [FromQuery] int limit = 300)
    {
        if (bangumiId <= 0)
        {
            return BadRequest(new { message = "Valid BangumiId is required" });
        }

        var hashes = await _subscriptionService.GetManualTaskHashesAsync(bangumiId, limit);
        return Ok(hashes);
    }

    /// <summary>
    /// Get download history for a subscription
    /// </summary>
    [HttpGet("{id}/history")]
    public async Task<ActionResult<List<DownloadHistoryResponse>>> GetHistory(int id, [FromQuery] int limit = 50)
    {
        var subscription = await _subscriptionService.GetSubscriptionByIdAsync(id);
        if (subscription == null)
        {
            return NotFound(new { message = $"Subscription {id} not found" });
        }

        var history = await _subscriptionService.GetDownloadHistoryAsync(id, limit);
        return Ok(history);
    }

    /// <summary>
    /// Ensure a subscription exists and is enabled
    /// </summary>
    [HttpPost("ensure")]
    public async Task<ActionResult<SubscriptionResponse>> Ensure([FromBody] CreateSubscriptionRequest request)
    {
        if (request.BangumiId <= 0)
        {
            return BadRequest(new { message = "Valid BangumiId is required" });
        }

        try
        {
            var subscription = await _subscriptionService.EnsureSubscriptionAsync(request);
            return Ok(subscription);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Cancel subscription and optionally delete related files
    /// </summary>
    [HttpPost("{id:int}/cancel")]
    public async Task<ActionResult<CancelSubscriptionResponse>> Cancel(
        [FromRoute] int id,
        [FromBody] CancelSubscriptionRequest request)
    {
        if (id <= 0)
        {
            return BadRequest(new { message = "Valid subscription id is required" });
        }

        if (request == null)
        {
            return BadRequest(new { message = "Request body is required" });
        }

        var action = request.Action?.Trim().ToLowerInvariant();
        if (action != CancelSubscriptionActionValues.DeleteFiles &&
            action != CancelSubscriptionActionValues.KeepFiles)
        {
            return BadRequest(new { message = "Action must be delete_files or keep_files" });
        }

        var result = await _subscriptionService.CancelSubscriptionAsync(
            id,
            deleteFiles: action == CancelSubscriptionActionValues.DeleteFiles);

        if (result == null)
        {
            return NotFound(new { message = $"Subscription {id} not found" });
        }

        return Ok(result);
    }

    /// <summary>
    /// Get anime list that has manual download tasks but no subscription record
    /// </summary>
    [HttpGet("manual-anime")]
    public async Task<ActionResult<List<ManualDownloadAnimeResponse>>> GetManualAnime([FromQuery] int limit = 200)
    {
        var safeLimit = Math.Clamp(limit, 1, 500);
        var results = await _subscriptionService.GetManualOnlyDownloadAnimesAsync(safeLimit);
        return Ok(results);
    }

    /// <summary>
    /// Get manual download history by anime Bangumi ID
    /// </summary>
    [HttpGet("manual-anime/{bangumiId:int}/history")]
    public async Task<ActionResult<List<DownloadHistoryResponse>>> GetManualAnimeHistory(
        [FromRoute] int bangumiId,
        [FromQuery] int limit = 50)
    {
        if (bangumiId <= 0)
        {
            return BadRequest(new { message = "Valid BangumiId is required" });
        }

        var safeLimit = Math.Clamp(limit, 1, 500);
        var history = await _subscriptionService.GetManualDownloadHistoryAsync(bangumiId, safeLimit);
        return Ok(history);
    }
}
