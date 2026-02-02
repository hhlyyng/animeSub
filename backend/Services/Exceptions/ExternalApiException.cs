namespace backend.Services.Exceptions;

/// <summary>
/// Exception thrown when external API calls fail (Bangumi, TMDB, AniList)
/// </summary>
public class ExternalApiException : ApiException
{
    public string ApiName { get; }
    public string? RequestUrl { get; }

    public ExternalApiException(
        string apiName,
        string message,
        string? requestUrl = null,
        Exception? innerException = null)
        : base(
            message,
            $"EXTERNAL_API_ERROR_{apiName.ToUpperInvariant()}",
            502, // Bad Gateway
            new { ApiName = apiName, RequestUrl = requestUrl },
            innerException)
    {
        ApiName = apiName;
        RequestUrl = requestUrl;
    }
}
