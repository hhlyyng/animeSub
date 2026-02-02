namespace backend.Models;

/// <summary>
/// Standardized error response format for all API errors
/// </summary>
public class ErrorResponse
{
    /// <summary>
    /// Indicates if the request was successful (always false for errors)
    /// </summary>
    public bool Success { get; set; } = false;

    /// <summary>
    /// Human-readable error message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Machine-readable error code (e.g., "MISSING_BANGUMI_TOKEN")
    /// </summary>
    public string ErrorCode { get; set; } = string.Empty;

    /// <summary>
    /// HTTP status code
    /// </summary>
    public int StatusCode { get; set; }

    /// <summary>
    /// Additional error details (validation errors, stack trace in dev, etc.)
    /// </summary>
    public object? Details { get; set; }

    /// <summary>
    /// Timestamp when the error occurred
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Request path that caused the error
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// Correlation ID for tracing the request across services
    /// </summary>
    public string? TraceId { get; set; }
}
