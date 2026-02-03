using System.Text.Json.Serialization;

namespace backend.Models.Dtos;

/// <summary>
/// Generic API response wrapper for consistent response format
/// </summary>
/// <typeparam name="T">Type of data payload</typeparam>
public class ApiResponseDto<T>
{
    /// <summary>
    /// Indicates if the request was successful
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>
    /// Response data payload
    /// </summary>
    [JsonPropertyName("data")]
    public T? Data { get; set; }

    /// <summary>
    /// Response metadata
    /// </summary>
    [JsonPropertyName("metadata")]
    public ResponseMetadataDto? Metadata { get; set; }

    /// <summary>
    /// Human-readable message
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>
    /// Create a success response
    /// </summary>
    public static ApiResponseDto<T> Ok(T data, string? message = null, ResponseMetadataDto? metadata = null)
    {
        return new ApiResponseDto<T>
        {
            Success = true,
            Data = data,
            Message = message,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Create an error response
    /// </summary>
    public static ApiResponseDto<T> Error(string message, ResponseMetadataDto? metadata = null)
    {
        return new ApiResponseDto<T>
        {
            Success = false,
            Data = default,
            Message = message,
            Metadata = metadata
        };
    }
}

/// <summary>
/// Response metadata for tracking data source and freshness
/// </summary>
public class ResponseMetadataDto
{
    /// <summary>
    /// Data source: "api", "cache", or "cachefallback"
    /// </summary>
    [JsonPropertyName("dataSource")]
    public string DataSource { get; set; } = "api";

    /// <summary>
    /// Whether the data is potentially stale
    /// </summary>
    [JsonPropertyName("isStale")]
    public bool IsStale { get; set; }

    /// <summary>
    /// When the data was last updated (ISO 8601)
    /// </summary>
    [JsonPropertyName("lastUpdated")]
    public string? LastUpdated { get; set; }

    /// <summary>
    /// Number of retry attempts made
    /// </summary>
    [JsonPropertyName("retryAttempts")]
    public int RetryAttempts { get; set; }
}

/// <summary>
/// Anime list data payload
/// </summary>
public class AnimeListDataDto
{
    /// <summary>
    /// Number of anime in the list
    /// </summary>
    [JsonPropertyName("count")]
    public int Count { get; set; }

    /// <summary>
    /// List of anime
    /// </summary>
    [JsonPropertyName("animes")]
    public List<AnimeInfoDto> Animes { get; set; } = new();
}
