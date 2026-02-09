namespace backend.Services.Exceptions;

/// <summary>
/// Raised when qBittorrent is offline/unreachable and requests are temporarily suspended.
/// </summary>
public class QBittorrentUnavailableException : ApiException
{
    public string Reason { get; }
    public DateTime RetryAfterUtc { get; }

    public QBittorrentUnavailableException(
        string message,
        string reason,
        DateTime retryAfterUtc,
        Exception? innerException = null)
        : base(
            message,
            errorCode: "QBITTORRENT_UNAVAILABLE",
            statusCode: 503,
            details: new
            {
                reason,
                retryAfterUtc
            },
            innerException: innerException)
    {
        Reason = reason;
        RetryAfterUtc = retryAfterUtc;
    }
}
