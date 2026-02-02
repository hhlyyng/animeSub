using System.Diagnostics;
using System.Text;

namespace backend.Middleware;

/// <summary>
/// Middleware that logs detailed information about HTTP requests and responses
/// Includes request body, response body, headers, and performance metrics
/// </summary>
public class RequestResponseLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestResponseLoggingMiddleware> _logger;
    private readonly IWebHostEnvironment _environment;

    public RequestResponseLoggingMiddleware(
        RequestDelegate next,
        ILogger<RequestResponseLoggingMiddleware> logger,
        IWebHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Start timing
        var stopwatch = Stopwatch.StartNew();

        // Log request details
        await LogRequest(context);

        // Capture original response body stream
        var originalResponseBody = context.Response.Body;

        try
        {
            // Replace response body stream with memory stream to capture response
            using var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            // Call next middleware
            await _next(context);

            // Log response details
            await LogResponse(context, stopwatch.ElapsedMilliseconds);

            // Copy captured response to original stream
            responseBody.Seek(0, SeekOrigin.Begin);
            await responseBody.CopyToAsync(originalResponseBody);
        }
        finally
        {
            // Restore original response body stream
            context.Response.Body = originalResponseBody;
            stopwatch.Stop();
        }
    }

    private async Task LogRequest(HttpContext context)
    {
        try
        {
            var request = context.Request;

            // Build request info
            var requestInfo = new Dictionary<string, object>
            {
                ["Method"] = request.Method,
                ["Path"] = request.Path.ToString(),
                ["QueryString"] = request.QueryString.ToString(),
                ["Scheme"] = request.Scheme,
                ["Host"] = request.Host.ToString(),
                ["ContentType"] = request.ContentType ?? "N/A",
                ["ContentLength"] = request.ContentLength ?? 0
            };

            // Log headers (exclude sensitive headers)
            if (_environment.IsDevelopment())
            {
                var headers = request.Headers
                    .Where(h => !IsSensitiveHeader(h.Key))
                    .ToDictionary(h => h.Key, h => h.Value.ToString());
                requestInfo["Headers"] = headers;
            }

            // Log request body for POST/PUT requests (only in development)
            if (_environment.IsDevelopment() &&
                (request.Method == "POST" || request.Method == "PUT") &&
                request.ContentLength > 0 &&
                request.ContentLength < 10000) // Max 10KB
            {
                request.EnableBuffering();
                var body = await ReadRequestBody(request);
                if (!string.IsNullOrWhiteSpace(body))
                {
                    requestInfo["Body"] = body;
                }
                request.Body.Seek(0, SeekOrigin.Begin);
            }

            _logger.LogInformation("HTTP Request: {@RequestInfo}", requestInfo);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error logging request details");
        }
    }

    private async Task LogResponse(HttpContext context, long elapsedMilliseconds)
    {
        try
        {
            var response = context.Response;

            // Build response info
            var responseInfo = new Dictionary<string, object>
            {
                ["StatusCode"] = response.StatusCode,
                ["ContentType"] = response.ContentType ?? "N/A",
                ["ContentLength"] = response.ContentLength ?? response.Body.Length,
                ["ElapsedMilliseconds"] = elapsedMilliseconds
            };

            // Add performance category
            responseInfo["PerformanceCategory"] = elapsedMilliseconds switch
            {
                < 100 => "Excellent",
                < 500 => "Good",
                < 1000 => "Acceptable",
                < 3000 => "Slow",
                _ => "Very Slow"
            };

            // Log response body (only in development and for small responses)
            if (_environment.IsDevelopment() &&
                response.ContentLength < 10000 &&
                response.Body.CanSeek)
            {
                response.Body.Seek(0, SeekOrigin.Begin);
                var body = await new StreamReader(response.Body).ReadToEndAsync();
                if (!string.IsNullOrWhiteSpace(body))
                {
                    responseInfo["Body"] = body.Length > 500 ? body.Substring(0, 500) + "..." : body;
                }
                response.Body.Seek(0, SeekOrigin.Begin);
            }

            // Choose log level based on status code
            var logLevel = response.StatusCode switch
            {
                >= 500 => LogLevel.Error,
                >= 400 => LogLevel.Warning,
                _ => LogLevel.Information
            };

            _logger.Log(logLevel, "HTTP Response: {@ResponseInfo}", responseInfo);

            // Log performance warning if slow
            if (elapsedMilliseconds > 3000)
            {
                _logger.LogWarning("Slow request detected: {Method} {Path} took {ElapsedMs}ms",
                    context.Request.Method,
                    context.Request.Path,
                    elapsedMilliseconds);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error logging response details");
        }
    }

    private async Task<string> ReadRequestBody(HttpRequest request)
    {
        try
        {
            using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
            return await reader.ReadToEndAsync();
        }
        catch
        {
            return string.Empty;
        }
    }

    private bool IsSensitiveHeader(string headerName)
    {
        var sensitiveHeaders = new[]
        {
            "Authorization",
            "X-Bangumi-Token",
            "X-TMDB-Token",
            "Cookie",
            "Set-Cookie",
            "X-API-Key"
        };

        return sensitiveHeaders.Contains(headerName, StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Extension method to register the request/response logging middleware
/// </summary>
public static class RequestResponseLoggingMiddlewareExtensions
{
    public static IApplicationBuilder UseRequestResponseLogging(this IApplicationBuilder app)
    {
        return app.UseMiddleware<RequestResponseLoggingMiddleware>();
    }
}
