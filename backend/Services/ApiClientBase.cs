using System.Text.Json;

namespace backend.Services;

/// <summary>
/// Abstract base class for all external API clients
/// Provides common functionality: HTTP client management, logging, error handling, token management
/// </summary>
/// <typeparam name="TClient">The concrete client type for logging</typeparam>
public abstract class ApiClientBase<TClient> where TClient : class
{
    protected readonly HttpClient HttpClient;
    protected readonly ILogger<TClient> Logger;
    protected string? Token;

    protected ApiClientBase(HttpClient httpClient, ILogger<TClient> logger, string baseUrl)
    {
        HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));

        HttpClient.BaseAddress = new Uri(baseUrl);
    }

    /// <summary>
    /// Set API authentication token
    /// </summary>
    public virtual void SetToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            Logger.LogWarning("Token is null or empty for {ClientType}", typeof(TClient).Name);
            return;
        }

        Token = token;

        // Update Authorization header
        if (HttpClient.DefaultRequestHeaders.Contains("Authorization"))
            HttpClient.DefaultRequestHeaders.Remove("Authorization");

        HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        Logger.LogInformation("{ClientType} token configured successfully", typeof(TClient).Name);
    }

    /// <summary>
    /// Execute an async operation with standardized error handling and logging
    /// </summary>
    protected async Task<T> ExecuteAsync<T>(
        Func<Task<T>> operation,
        string operationName,
        Dictionary<string, object>? logContext = null)
    {
        try
        {
            if (logContext != null)
            {
                using (Logger.BeginScope(logContext))
                {
                    Logger.LogInformation("Starting {Operation} in {ClientType}", operationName, typeof(TClient).Name);
                }
            }
            else
            {
                Logger.LogInformation("Starting {Operation} in {ClientType}", operationName, typeof(TClient).Name);
            }

            var result = await operation().ConfigureAwait(false);

            Logger.LogInformation("Completed {Operation} successfully", operationName);
            return result;
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "{Operation} HTTP request failed in {ClientType}", operationName, typeof(TClient).Name);
            throw;
        }
        catch (JsonException ex)
        {
            Logger.LogError(ex, "{Operation} JSON parsing failed in {ClientType}", operationName, typeof(TClient).Name);
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "{Operation} unexpected error in {ClientType}", operationName, typeof(TClient).Name);
            throw;
        }
    }

    /// <summary>
    /// Execute an async operation with standardized error handling, returning null on failure instead of throwing
    /// </summary>
    protected async Task<T?> ExecuteWithGracefulFallbackAsync<T>(
        Func<Task<T?>> operation,
        string operationName,
        Dictionary<string, object>? logContext = null) where T : class
    {
        try
        {
            if (logContext != null)
            {
                using (Logger.BeginScope(logContext))
                {
                    Logger.LogInformation("Starting {Operation} in {ClientType}", operationName, typeof(TClient).Name);
                }
            }
            else
            {
                Logger.LogInformation("Starting {Operation} in {ClientType}", operationName, typeof(TClient).Name);
            }

            var result = await operation().ConfigureAwait(false);

            if (result != null)
            {
                Logger.LogInformation("Completed {Operation} successfully", operationName);
            }
            else
            {
                Logger.LogWarning("{Operation} returned null result", operationName);
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "{Operation} failed in {ClientType}, returning null for graceful degradation",
                operationName, typeof(TClient).Name);
            return null;
        }
    }

    /// <summary>
    /// Validate that token is set before making API calls
    /// </summary>
    protected void EnsureTokenSet()
    {
        if (string.IsNullOrEmpty(Token))
        {
            throw new InvalidOperationException(
                $"Token must be set before calling API methods in {typeof(TClient).Name}");
        }
    }
}
