using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using backend.Models;
using backend.Models.Configuration;
using backend.Services.Interfaces;

namespace backend.Services.Implementations
{
    /// <summary>
    /// AniList GraphQL API client implementation for fetching English anime metadata
    /// </summary>
    public class AniListClient : ApiClientBase<AniListClient>, IAniListClient
    {
        public AniListClient(
            HttpClient httpClient,
            ILogger<AniListClient> logger,
            IOptions<ApiConfiguration> config)
            : base(httpClient, logger, config.Value.AniList.BaseUrl)
        {
            HttpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            HttpClient.DefaultRequestHeaders.Add("User-Agent", "Anime-Sub");
            HttpClient.Timeout = TimeSpan.FromSeconds(config.Value.AniList.TimeoutSeconds);
        }

        public Task<AniListAnimeInfo?> GetAnimeInfoAsync(string japaneseTitle) =>
            ExecuteWithGracefulFallbackAsync(async () =>
            {
                if (string.IsNullOrWhiteSpace(japaneseTitle))
                    throw new ArgumentException("Japanese title cannot be null or empty.", nameof(japaneseTitle));

                const string query = @"
                    query ($search: String) {
                        Media(search: $search, type: ANIME) {
                            title {
                                english
                            }
                            description
                            coverImage {
                                extraLarge
                            }
                            siteUrl
                        }
                    }";

                var payload = new
                {
                    query = query,
                    variables = new { search = japaneseTitle }
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json"
                );

                using var resp = await HttpClient.PostAsync("", content);

                if (!resp.IsSuccessStatusCode)
                {
                    Logger.LogError("AniList API returned status {StatusCode} for title: {Title}",
                        resp.StatusCode, japaneseTitle);
                    throw new HttpRequestException($"AniList API Error: HTTP {resp.StatusCode}");
                }

                using var stream = await resp.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(stream);

                if (!doc.RootElement.TryGetProperty("data", out var dataElement))
                {
                    Logger.LogError("Invalid AniList response format for title: {Title}", japaneseTitle);
                    throw new JsonException("Missing 'data' property in AniList response");
                }

                var media = dataElement.GetProperty("Media");

                if (media.ValueKind == JsonValueKind.Null)
                {
                    Logger.LogInformation("No AniList data found for title: {Title}", japaneseTitle);
                    return null;
                }

                Logger.LogInformation("Found AniList info for '{Title}'", japaneseTitle);

                return new AniListAnimeInfo
                {
                    EnglishTitle = media.GetProperty("title").GetProperty("english").GetString() ?? string.Empty,
                    EnglishSummary = media.GetProperty("description").GetString() ?? string.Empty,
                    CoverUrl = media.GetProperty("coverImage").GetProperty("extraLarge").GetString() ?? string.Empty,
                    OriSiteUrl = media.GetProperty("siteUrl").GetString() ?? string.Empty
                };
            }, "GetAnimeInfo", new Dictionary<string, object> { ["Title"] = japaneseTitle });
    }
}
