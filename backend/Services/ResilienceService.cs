using Polly;
using Polly.Retry;

namespace backend.Services;

/// <summary>
/// Provides resilience policies for external API calls
/// </summary>
public interface IResilienceService
{
    /// <summary>
    /// Execute an async operation with retry policy
    /// Max 3 retries within 1 minute with exponential backoff
    /// </summary>
    Task<T> ExecuteWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute an async operation with retry, returning result and retry count
    /// </summary>
    Task<(T Result, int RetryCount, bool Success)> ExecuteWithRetryAndMetadataAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resilience service using Polly for retry policies
/// Policy: Max 3 retries within 1 minute (intervals: 5s, 15s, 30s)
/// </summary>
public class ResilienceService : IResilienceService
{
    private readonly ILogger<ResilienceService> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;

    public ResilienceService(ILogger<ResilienceService> logger)
    {
        _logger = logger;

        // Create retry policy: 3 retries with exponential backoff
        // Total max time: 5 + 15 + 30 = 50 seconds (within 1 minute)
        _retryPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<TaskCanceledException>()
            .Or<TimeoutException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => retryAttempt switch
                {
                    1 => TimeSpan.FromSeconds(5),   // First retry after 5s
                    2 => TimeSpan.FromSeconds(15),  // Second retry after 15s
                    3 => TimeSpan.FromSeconds(30),  // Third retry after 30s
                    _ => TimeSpan.FromSeconds(30)
                },
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    _logger.LogWarning(
                        "Retry {RetryCount}/3 for {Operation} after {Delay}s due to: {Error}",
                        retryCount,
                        context.OperationKey,
                        timeSpan.TotalSeconds,
                        exception.Message);
                });
    }

    public async Task<T> ExecuteWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        var context = new Context(operationName);

        return await _retryPolicy.ExecuteAsync(
            async (ctx, ct) =>
            {
                _logger.LogInformation("Executing {Operation}", operationName);
                return await operation(ct);
            },
            context,
            cancellationToken);
    }

    public async Task<(T Result, int RetryCount, bool Success)> ExecuteWithRetryAndMetadataAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        var retryCount = 0;
        var context = new Context(operationName);

        // Create policy with retry count tracking
        var policyWithMetrics = Policy
            .Handle<HttpRequestException>()
            .Or<TaskCanceledException>()
            .Or<TimeoutException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => retryAttempt switch
                {
                    1 => TimeSpan.FromSeconds(5),
                    2 => TimeSpan.FromSeconds(15),
                    3 => TimeSpan.FromSeconds(30),
                    _ => TimeSpan.FromSeconds(30)
                },
                onRetry: (exception, timeSpan, currentRetry, ctx) =>
                {
                    retryCount = currentRetry;
                    _logger.LogWarning(
                        "Retry {RetryCount}/3 for {Operation} after {Delay}s due to: {Error}",
                        currentRetry,
                        ctx.OperationKey,
                        timeSpan.TotalSeconds,
                        exception.Message);
                });

        try
        {
            var result = await policyWithMetrics.ExecuteAsync(
                async (ctx, ct) =>
                {
                    _logger.LogInformation("Executing {Operation}", operationName);
                    return await operation(ct);
                },
                context,
                cancellationToken);

            return (result, retryCount, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "All retries failed for {Operation}", operationName);
            return (default!, retryCount + 1, false);  // +1 because we also count the final failed attempt
        }
    }
}
