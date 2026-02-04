using System.Text.Json;
using Microsoft.Extensions.Options;
using backend.Models;
using backend.Models.Configuration;
using backend.Services.Interfaces;

namespace backend.Services.Implementations
{
    /// <summary>
    /// TMDB API client implementation for fetching English translations and backdrop images
    /// </summary>
    public class TMDBClient : ApiClientBase<TMDBClient>, ITMDBClient
    {
        private readonly string _imageBaseUrl;

        public TMDBClient(
            HttpClient httpClient,
            ILogger<TMDBClient> logger,
            IOptions<ApiConfiguration> config)
            : base(httpClient, logger, config.Value.TMDB.BaseUrl)
        {
            _imageBaseUrl = config.Value.TMDB.ImageBaseUrl;
            HttpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            HttpClient.Timeout = TimeSpan.FromSeconds(config.Value.TMDB.TimeoutSeconds);
        }

        public override void SetToken(string? token)
        {
            // TMDB token is optional - override to not log warning if missing
            if (string.IsNullOrWhiteSpace(token))
            {
                Logger.LogInformation("TMDB token not provided, some features will be unavailable");
                return;
            }

            base.SetToken(token);
        }

        public Task<TMDBAnimeInfo?> GetAnimeSummaryAndBackdropAsync(string title) =>
            ExecuteWithGracefulFallbackAsync(async () =>
            {
                if (string.IsNullOrWhiteSpace(title))
                    throw new ArgumentException("Title cannot be null or empty.", nameof(title));

                if (string.IsNullOrEmpty(Token))
                {
                    Logger.LogInformation("TMDB token not set, skipping search for '{Title}'", title);
                    return null;
                }

                const string language = "en-US";
                var url = $"search/tv?query={Uri.EscapeDataString(title)}&language={language}&page=1&include_adult=false";

                using var resp = await HttpClient.GetAsync(url);
                resp.EnsureSuccessStatusCode();

                using var stream = await resp.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(stream);

                if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                {
                    Logger.LogInformation("No TMDB results found for title: {Title}", title);
                    return new TMDBAnimeInfo { EnglishSummary = "No result found in TMDB.", BackdropUrl = "" };
                }

                // Prioritize anime results: prefer Animation genre (16) + Japanese origin
                JsonElement? bestMatch = null;

                foreach (var item in results.EnumerateArray())
                {
                    bool isAnimation = false;
                    bool isJapanese = false;

                    // Check genre_ids for Animation (16)
                    if (item.TryGetProperty("genre_ids", out var genres) && genres.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var genre in genres.EnumerateArray())
                        {
                            if (genre.TryGetInt32(out var genreId) && genreId == 16)
                            {
                                isAnimation = true;
                                break;
                            }
                        }
                    }

                    // Check origin_country for JP
                    if (item.TryGetProperty("origin_country", out var countries) && countries.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var country in countries.EnumerateArray())
                        {
                            if (country.GetString() == "JP")
                            {
                                isJapanese = true;
                                break;
                            }
                        }
                    }

                    // Perfect match: Animation + Japanese
                    if (isAnimation && isJapanese)
                    {
                        bestMatch = item;
                        Logger.LogInformation("Found Japanese anime match for '{Title}'", title);
                        break;
                    }

                    // Second priority: Animation (any origin)
                    if (isAnimation && bestMatch == null)
                    {
                        bestMatch = item;
                    }
                }

                // Fallback to first result if no anime found
                var first = bestMatch ?? results[0];
                if (bestMatch == null)
                {
                    Logger.LogInformation("No anime match found for '{Title}', using first result", title);
                }

                int tvId = first.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var idVal) ? idVal : 0;
                string? enTitleFromSearch = first.TryGetProperty("name", out var nm) ? nm.GetString() : null;
                string enOverviewFromSearch = first.TryGetProperty("overview", out var ov)
                    ? (ov.GetString() ?? "No English summary available.")
                    : "No English summary available.";

                string backdropUrl = "";
                if (first.TryGetProperty("backdrop_path", out var bp) && bp.ValueKind == JsonValueKind.String)
                {
                    string? path = bp.GetString();
                    if (!string.IsNullOrWhiteSpace(path))
                        backdropUrl = $"{_imageBaseUrl}original{path}";
                }

                string enTitle = enTitleFromSearch ?? "";
                string zhTitle = "";
                string enOverview = enOverviewFromSearch;
                string zhOverview = "";

                // Fetch translations
                if (tvId > 0)
                {
                    var tUrl = $"tv/{tvId}/translations";
                    using var tResp = await HttpClient.GetAsync(tUrl);
                    tResp.EnsureSuccessStatusCode();

                    using var tStream = await tResp.Content.ReadAsStreamAsync();
                    using var tDoc = await JsonDocument.ParseAsync(tStream);

                    if (tDoc.RootElement.TryGetProperty("translations", out var trans) && trans.ValueKind == JsonValueKind.Array)
                    {
                        var en = trans.EnumerateArray()
                            .FirstOrDefault(x => x.TryGetProperty("iso_639_1", out var l) && l.GetString() == "en");

                        JsonElement zh = default;
                        bool hasZh = false;

                        foreach (var cand in trans.EnumerateArray())
                        {
                            if (cand.TryGetProperty("iso_639_1", out var l) && l.GetString() == "zh")
                            {
                                string? region = cand.TryGetProperty("iso_3166_1", out var r) ? r.GetString() : null;
                                if (region == "CN")
                                {
                                    zh = cand;
                                    hasZh = true;
                                    break;
                                }
                                if (!hasZh)
                                {
                                    zh = cand;
                                    hasZh = true;
                                }
                            }
                        }

                        if (en.ValueKind != JsonValueKind.Undefined &&
                            en.TryGetProperty("data", out var enData) && enData.ValueKind == JsonValueKind.Object)
                        {
                            if (enData.TryGetProperty("name", out var enNameEl))
                                enTitle = enNameEl.GetString() ?? enTitle;
                            if (enData.TryGetProperty("overview", out var enOvEl))
                                enOverview = enOvEl.GetString() ?? enOverview;
                        }

                        if (hasZh && zh.TryGetProperty("data", out var zhData) && zhData.ValueKind == JsonValueKind.Object)
                        {
                            if (zhData.TryGetProperty("name", out var zhNameEl))
                                zhTitle = zhNameEl.GetString() ?? zhTitle;
                            if (zhData.TryGetProperty("overview", out var zhOvEl))
                                zhOverview = zhOvEl.GetString() ?? zhOverview;
                        }
                    }
                }

                Logger.LogInformation("Found TMDB info for '{Title}' (ID: {TvId})", title, tvId);

                return new TMDBAnimeInfo
                {
                    TMDBID = tvId.ToString(),
                    EnglishTitle = enTitle,
                    ChineseTitle = zhTitle,
                    EnglishSummary = string.IsNullOrWhiteSpace(enOverview) ? "No English summary available." : enOverview,
                    ChineseSummary = zhOverview ?? "",
                    BackdropUrl = backdropUrl,
                    OriSiteUrl = tvId > 0 ? $"https://www.themoviedb.org/tv/{tvId}" : ""
                };
            }, "GetAnimeSummaryAndBackdrop", new Dictionary<string, object> { ["Title"] = title });

        public Task<List<string>> GetMovieImagesAsync(int movieId, string imageType = "posters", string size = "w500", string language = "en-US") =>
            ExecuteAsync(async () =>
            {
                var url = $"movie/{movieId}/images?language={language}&include_image_language=en,null";
                var response = await HttpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<JsonElement>(content);

                var imageUrls = new List<string>();
                if (data.TryGetProperty(imageType, out var images))
                {
                    foreach (var image in images.EnumerateArray())
                    {
                        var filePath = image.GetProperty("file_path").GetString();
                        imageUrls.Add($"{_imageBaseUrl}{size}{filePath}");
                    }
                }

                return imageUrls;
            }, "GetMovieImages", new Dictionary<string, object> { ["MovieId"] = movieId, ["ImageType"] = imageType });

        public Task<List<string>> GetTVImagesAsync(int tvId, string imageType = "posters", string size = "w500", string language = "en-US") =>
            ExecuteAsync(async () =>
            {
                var url = $"tv/{tvId}/images?language={language}&include_image_language=en,null";
                var response = await HttpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<JsonElement>(content);

                var imageUrls = new List<string>();
                if (data.TryGetProperty(imageType, out var images))
                {
                    foreach (var image in images.EnumerateArray())
                    {
                        var filePath = image.GetProperty("file_path").GetString();
                        imageUrls.Add($"{_imageBaseUrl}{size}{filePath}");
                    }
                }

                return imageUrls;
            }, "GetTVImages", new Dictionary<string, object> { ["TvId"] = tvId, ["ImageType"] = imageType });

        public Task<List<string>> GetPersonImagesAsync(int personId, string size = "w185") =>
            ExecuteAsync(async () =>
            {
                var response = await HttpClient.GetAsync($"person/{personId}/images");
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<JsonElement>(content);

                var profileUrls = new List<string>();
                if (data.TryGetProperty("profiles", out var profiles))
                {
                    foreach (var profile in profiles.EnumerateArray())
                    {
                        var filePath = profile.GetProperty("file_path").GetString();
                        profileUrls.Add($"{_imageBaseUrl}{size}{filePath}");
                    }
                }

                return profileUrls;
            }, "GetPersonImages", new Dictionary<string, object> { ["PersonId"] = personId });
    }
}
