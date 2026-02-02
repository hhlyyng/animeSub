using System.Net;
using System.Text.Json;
using backend.Models;
using backend.Services.Exceptions;

namespace backend.Middleware;

/// <summary>
/// Global exception handling middleware
/// Catches all unhandled exceptions and converts them to standardized error responses
/// </summary>
public class ExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlerMiddleware> _logger;
    private readonly IWebHostEnvironment _environment;

    public ExceptionHandlerMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlerMiddleware> logger,
        IWebHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception occurred: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var errorResponse = CreateErrorResponse(context, exception);

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = errorResponse.StatusCode;

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = _environment.IsDevelopment()
        };

        var json = JsonSerializer.Serialize(errorResponse, options);
        await context.Response.WriteAsync(json);
    }

    private ErrorResponse CreateErrorResponse(HttpContext context, Exception exception)
    {
        var errorResponse = new ErrorResponse
        {
            Path = context.Request.Path,
            TraceId = context.TraceIdentifier
        };

        switch (exception)
        {
            // IMPORTANT: Handle specific exceptions BEFORE their base classes

            case ValidationException validationEx:
                // Validation errors
                errorResponse.Message = validationEx.Message;
                errorResponse.ErrorCode = "VALIDATION_ERROR";
                errorResponse.StatusCode = 400;
                errorResponse.Details = validationEx.ValidationErrors;
                break;

            case InvalidCredentialsException credEx:
                // Authentication/authorization errors
                errorResponse.Message = credEx.Message;
                errorResponse.ErrorCode = "INVALID_CREDENTIALS";
                errorResponse.StatusCode = 401;
                errorResponse.Details = new { CredentialType = credEx.CredentialType };
                break;

            case ExternalApiException externalEx:
                // External API failures
                errorResponse.Message = externalEx.Message;
                errorResponse.ErrorCode = externalEx.ErrorCode;
                errorResponse.StatusCode = 502;
                errorResponse.Details = new
                {
                    ApiName = externalEx.ApiName,
                    RequestUrl = externalEx.RequestUrl
                };
                break;

            case ApiException apiEx:
                // Generic API exceptions (must be after specific subclasses)
                errorResponse.Message = apiEx.Message;
                errorResponse.ErrorCode = apiEx.ErrorCode;
                errorResponse.StatusCode = apiEx.StatusCode;
                errorResponse.Details = apiEx.Details;
                break;

            case HttpRequestException httpEx:
                // Generic HTTP errors
                errorResponse.Message = "External service request failed";
                errorResponse.ErrorCode = "HTTP_REQUEST_ERROR";
                errorResponse.StatusCode = 502;
                errorResponse.Details = _environment.IsDevelopment()
                    ? new { Message = httpEx.Message }
                    : null;
                break;

            case JsonException jsonEx:
                // JSON parsing errors
                errorResponse.Message = "Invalid response format from external service";
                errorResponse.ErrorCode = "JSON_PARSE_ERROR";
                errorResponse.StatusCode = 502;
                errorResponse.Details = _environment.IsDevelopment()
                    ? new { Message = jsonEx.Message }
                    : null;
                break;

            case OperationCanceledException:
                // Request timeout or cancellation
                errorResponse.Message = "Request timeout or cancelled";
                errorResponse.ErrorCode = "REQUEST_TIMEOUT";
                errorResponse.StatusCode = 408;
                break;

            case ArgumentException argEx:
                // Invalid arguments (usually validation)
                errorResponse.Message = argEx.Message;
                errorResponse.ErrorCode = "INVALID_ARGUMENT";
                errorResponse.StatusCode = 400;
                errorResponse.Details = _environment.IsDevelopment()
                    ? new { ParamName = argEx.ParamName }
                    : null;
                break;

            case UnauthorizedAccessException:
                // Authorization failures
                errorResponse.Message = "Unauthorized access";
                errorResponse.ErrorCode = "UNAUTHORIZED";
                errorResponse.StatusCode = 403;
                break;

            default:
                // Catch-all for unexpected errors
                errorResponse.Message = "An unexpected error occurred";
                errorResponse.ErrorCode = "INTERNAL_ERROR";
                errorResponse.StatusCode = 500;

                // Only include stack trace in development
                if (_environment.IsDevelopment())
                {
                    errorResponse.Details = new
                    {
                        Type = exception.GetType().Name,
                        Message = exception.Message,
                        StackTrace = exception.StackTrace
                    };
                }
                break;
        }

        return errorResponse;
    }
}

/// <summary>
/// Extension method to register the exception handler middleware
/// </summary>
public static class ExceptionHandlerMiddlewareExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ExceptionHandlerMiddleware>();
    }
}
