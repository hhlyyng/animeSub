namespace backend.Services.Exceptions;

/// <summary>
/// Base exception for all API-related errors
/// </summary>
public class ApiException : Exception
{
    public string ErrorCode { get; }
    public int StatusCode { get; }
    public object? Details { get; }

    public ApiException(
        string message,
        string errorCode,
        int statusCode = 500,
        object? details = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
        Details = details;
    }
}
