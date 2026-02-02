namespace backend.Services.Exceptions;

/// <summary>
/// Exception specific to Bangumi API failures
/// </summary>
public class BangumiApiException : ExternalApiException
{
    public BangumiApiException(
        string message,
        string? requestUrl = null,
        Exception? innerException = null)
        : base("Bangumi", message, requestUrl, innerException)
    {
    }
}
