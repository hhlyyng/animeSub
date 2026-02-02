using System.Diagnostics;

namespace backend.Middleware;

/// <summary>
/// Middleware that monitors request performance and logs slow requests
/// Tracks request duration and adds performance headers to responses
/// </summary>
public class PerformanceMonitoringMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<PerformanceMonitoringMiddleware> _logger;
    private const int SlowRequestThresholdMs = 1000; // 1 second

    public PerformanceMonitoringMiddleware(
        RequestDelegate next,
        ILogger<PerformanceMonitoringMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestPath = context.Request.Path;
        var requestMethod = context.Request.Method;

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            var elapsedMs = stopwatch.ElapsedMilliseconds;

            // Add performance timing header
            context.Response.Headers.TryAdd("X-Response-Time-Ms", elapsedMs.ToString());

            // Log performance metrics
            LogPerformanceMetrics(requestMethod, requestPath, elapsedMs, context.Response.StatusCode);
        }
    }

    private void LogPerformanceMetrics(string method, string path, long elapsedMs, int statusCode)
    {
        var performanceData = new
        {
            Method = method,
            Path = path.ToString(),
            DurationMs = elapsedMs,
            StatusCode = statusCode,
            Category = GetPerformanceCategory(elapsedMs)
        };

        if (elapsedMs > SlowRequestThresholdMs)
        {
            _logger.LogWarning("Slow request detected: {Method} {Path} completed in {DurationMs}ms [Status: {StatusCode}]",
                method, path, elapsedMs, statusCode);
        }
        else
        {
            _logger.LogInformation("Request performance: {Method} {Path} completed in {DurationMs}ms [Status: {StatusCode}]",
                method, path, elapsedMs, statusCode);
        }

        // Log structured metrics for monitoring systems
        _logger.LogInformation("Performance metrics: {@PerformanceData}", performanceData);
    }

    private string GetPerformanceCategory(long elapsedMs)
    {
        return elapsedMs switch
        {
            < 100 => "Excellent",
            < 500 => "Good",
            < 1000 => "Acceptable",
            < 3000 => "Slow",
            _ => "Critical"
        };
    }
}

/// <summary>
/// Extension method to register the performance monitoring middleware
/// </summary>
public static class PerformanceMonitoringMiddlewareExtensions
{
    public static IApplicationBuilder UsePerformanceMonitoring(this IApplicationBuilder app)
    {
        return app.UseMiddleware<PerformanceMonitoringMiddleware>();
    }
}
