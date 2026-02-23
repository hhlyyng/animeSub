using System.Text.Json;
using Microsoft.Extensions.Options;
using backend.Models.Configuration;
using backend.Models.Jikan;
using backend.Services.Interfaces;

namespace backend.Services.Implementations;

/// <summary>
/// Jikan API client implementation for fetching anime data from MyAnimeList
/// Jikan is a free REST API for MyAnimeList with rate limiting (3 req/s)
/// </summary>
public class JikanClient : ApiClientBase<JikanClient>, IJikanClient
{
    public JikanClient(
        HttpClient httpClient,
        ILogger<JikanClient> logger,
        IOptions<ApiConfiguration> config)
        : base(httpClient, logger, config.Value.Jikan.BaseUrl)
    {
        HttpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        HttpClient.DefaultRequestHeaders.Add("User-Agent", "Anime-Sub (https://github.com/hhlyyng/anime-subscription)");
        HttpClient.Timeout = TimeSpan.FromSeconds(config.Value.Jikan.TimeoutSeconds);
    }

    public Task<List<JikanAnimeInfo>> GetTopAnimeAsync(int limit = 10) =>
        ExecuteAsync(async () =>
        {
            // Jikan API: GET /top/anime?limit=N
            // Returns top anime sorted by MAL score
            var response = await HttpClient.GetAsync($"top/anime?limit={limit}");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JikanTopAnimeResponse>(content);

            if (result?.Data == null)
            {
                Logger.LogWarning("Jikan API returned null or empty data");
                return new List<JikanAnimeInfo>();
            }

            Logger.LogInformation("Retrieved {Count} top anime from Jikan/MAL", result.Data.Count);
            return result.Data;
        }, "GetTopAnime");

    public Task<List<JikanAnimeInfo>> GetTopAnimePageAsync(int page, int limit = 25) =>
        ExecuteAsync(async () =>
        {
            var response = await HttpClient.GetAsync($"top/anime?page={page}&limit={limit}");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JikanTopAnimeResponse>(content);

            if (result?.Data == null)
            {
                Logger.LogWarning("Jikan API returned null data for page {Page}", page);
                return new List<JikanAnimeInfo>();
            }

            Logger.LogInformation("Retrieved {Count} anime from Jikan page {Page}", result.Data.Count, page);
            return result.Data;
        }, $"GetTopAnimePage({page})");

    public async Task<JsonElement?> GetAnimeDetailAsync(int malId)
    {
        if (malId <= 0)
        {
            return null;
        }

        try
        {
            var response = await HttpClient.GetAsync($"anime/{malId}");
            if (!response.IsSuccessStatusCode)
            {
                Logger.LogWarning("GetAnimeDetail({MalId}) returned status {StatusCode}", malId, response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var root = JsonDocument.Parse(content).RootElement;
            if (!root.TryGetProperty("data", out var data))
            {
                Logger.LogWarning("GetAnimeDetail({MalId}) response missing data payload", malId);
                return null;
            }

            return data.Clone();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "GetAnimeDetail({MalId}) failed", malId);
            return null;
        }
    }
}
