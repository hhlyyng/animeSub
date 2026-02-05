using System.Text.Json;
using Microsoft.Extensions.Options;
using backend.Models;
using backend.Models.Configuration;
using backend.Services.Interfaces;
using backend.Services.Utilities;

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

        public Task<TMDBAnimeInfo?> GetAnimeSummaryAndBackdropAsync(string title, string? airDate = null) =>
            ExecuteWithGracefulFallbackAsync(async () =>
            {
                if (string.IsNullOrWhiteSpace(title))
                    throw new ArgumentException("Title cannot be null or empty.", nameof(title));

                if (string.IsNullOrEmpty(Token))
                {
                    Logger.LogInformation("TMDB token not set, skipping search for '{Title}'", title);
                    return null;
                }

                // Extract year from airDate (format: YYYY-MM-DD)
                int? year = ExtractYear(airDate);

                // Clean title (remove season suffix)
                var (cleanedTitle, wasCleaned) = TitleCleaner.RemoveSeasonSuffix(title);

                // Three-layer fallback search strategy:
                // 1. Original title + year filter
                // 2. Cleaned title + year filter (if title was cleaned)
                // 3. Cleaned/original title without year filter

                // Layer 1: Original title + year
                var searchResult = await SearchTMDBAsync(title, year);

                // Layer 2: Cleaned title + year (only if title was cleaned)
                if (searchResult == null && wasCleaned)
                {
                    Logger.LogInformation("No results for '{OriginalTitle}', trying cleaned title '{CleanedTitle}'",
                        title, cleanedTitle);
                    searchResult = await SearchTMDBAsync(cleanedTitle, year);
                }

                // Layer 3: Without year filter (use cleaned title if available)
                if (searchResult == null && year.HasValue)
                {
                    var titleToSearch = wasCleaned ? cleanedTitle : title;
                    Logger.LogInformation("No results with year filter, trying '{Title}' without year filter",
                        titleToSearch);
                    searchResult = await SearchTMDBAsync(titleToSearch, null);
                }

                // No results found after all attempts
                if (searchResult == null)
                {
                    Logger.LogInformation("No TMDB results found for title: {Title}", title);
                    return new TMDBAnimeInfo { EnglishSummary = "No result found in TMDB.", BackdropUrl = "" };
                }

                var first = searchResult.Value;

                // Detect if this is a movie or TV show
                // Movies have "title" and "release_date", TV shows have "name" and "first_air_date"
                bool isMovie = first.TryGetProperty("title", out _);
                string mediaType = isMovie ? "movie" : "tv";

                int mediaId = first.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var idVal) ? idVal : 0;

                // Movies use "title", TV shows use "name"
                string? enTitleFromSearch = isMovie
                    ? (first.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null)
                    : (first.TryGetProperty("name", out var nm) ? nm.GetString() : null);

                string enOverviewFromSearch = first.TryGetProperty("overview", out var ov)
                    ? (ov.GetString() ?? "No English summary available.")
                    : "No English summary available.";

                string backdropUrl = "";
                if (first.TryGetProperty("backdrop_path", out var bp))
                {
                    if (bp.ValueKind == JsonValueKind.String)
                    {
                        string? path = bp.GetString();
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            backdropUrl = $"{_imageBaseUrl}original{path}";
                            Logger.LogInformation("Found backdrop for '{Title}': {BackdropPath}", title, path);
                        }
                        else
                        {
                            Logger.LogWarning("Backdrop path is empty for '{Title}'", title);
                        }
                    }
                    else if (bp.ValueKind == JsonValueKind.Null)
                    {
                        Logger.LogWarning("Backdrop path is null in search results for '{Title}' (ID: {MediaId})", title, mediaId);
                    }
                }
                else
                {
                    Logger.LogWarning("No backdrop_path property in search results for '{Title}'", title);
                }

                // Fallback: If no backdrop in search results, try fetching from details endpoint
                if (string.IsNullOrEmpty(backdropUrl) && mediaId > 0)
                {
                    try
                    {
                        var detailsUrl = $"{mediaType}/{mediaId}";
                        Logger.LogInformation("Fetching {MediaType} details for backdrop: {Url}", mediaType, detailsUrl);

                        using var detailsResp = await HttpClient.GetAsync(detailsUrl);
                        if (detailsResp.IsSuccessStatusCode)
                        {
                            using var detailsStream = await detailsResp.Content.ReadAsStreamAsync();
                            using var detailsDoc = await JsonDocument.ParseAsync(detailsStream);

                            if (detailsDoc.RootElement.TryGetProperty("backdrop_path", out var detailsBp) &&
                                detailsBp.ValueKind == JsonValueKind.String)
                            {
                                string? detailsPath = detailsBp.GetString();
                                if (!string.IsNullOrWhiteSpace(detailsPath))
                                {
                                    backdropUrl = $"{_imageBaseUrl}original{detailsPath}";
                                    Logger.LogInformation("Found backdrop from details for '{Title}': {BackdropPath}", title, detailsPath);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Failed to fetch {MediaType} details for backdrop (ID: {MediaId})", mediaType, mediaId);
                    }
                }

                string enTitle = enTitleFromSearch ?? "";
                string zhTitle = "";
                string enOverview = enOverviewFromSearch;
                string zhOverview = "";

                // Fetch translations (use appropriate endpoint for movie vs TV)
                if (mediaId > 0)
                {
                    var tUrl = $"{mediaType}/{mediaId}/translations";
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

                        // Movies use "title" in translations, TV shows use "name"
                        var titleKey = isMovie ? "title" : "name";

                        if (en.ValueKind != JsonValueKind.Undefined &&
                            en.TryGetProperty("data", out var enData) && enData.ValueKind == JsonValueKind.Object)
                        {
                            if (enData.TryGetProperty(titleKey, out var enNameEl))
                                enTitle = enNameEl.GetString() ?? enTitle;
                            if (enData.TryGetProperty("overview", out var enOvEl))
                                enOverview = enOvEl.GetString() ?? enOverview;
                        }

                        if (hasZh && zh.TryGetProperty("data", out var zhData) && zhData.ValueKind == JsonValueKind.Object)
                        {
                            if (zhData.TryGetProperty(titleKey, out var zhNameEl))
                                zhTitle = zhNameEl.GetString() ?? zhTitle;
                            if (zhData.TryGetProperty("overview", out var zhOvEl))
                                zhOverview = zhOvEl.GetString() ?? zhOverview;
                        }
                    }

                    // Try to get season-specific backdrop for multi-season anime (TV only, not movies)
                    if (!isMovie && year.HasValue)
                    {
                        try
                        {
                            var detailUrl = $"tv/{mediaId}";
                            using var detailResp = await HttpClient.GetAsync(detailUrl);

                            if (detailResp.IsSuccessStatusCode)
                            {
                                using var detailStream = await detailResp.Content.ReadAsStreamAsync();
                                using var detailDoc = await JsonDocument.ParseAsync(detailStream);

                                if (detailDoc.RootElement.TryGetProperty("seasons", out var seasons) &&
                                    seasons.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var season in seasons.EnumerateArray())
                                    {
                                        var seasonAirDate = season.TryGetProperty("air_date", out var sad)
                                            ? sad.GetString()
                                            : null;

                                        // Match season by year
                                        if (!string.IsNullOrEmpty(seasonAirDate) &&
                                            seasonAirDate.StartsWith(year.Value.ToString()))
                                        {
                                            int seasonNumber = season.TryGetProperty("season_number", out var sn)
                                                ? sn.GetInt32()
                                                : 0;

                                            // Skip season 0 (specials)
                                            if (seasonNumber == 0)
                                                continue;

                                            // Try to get season-specific images
                                            var seasonImgUrl = $"tv/{mediaId}/season/{seasonNumber}/images";
                                            using var seasonImgResp = await HttpClient.GetAsync(seasonImgUrl);

                                            if (seasonImgResp.IsSuccessStatusCode)
                                            {
                                                using var seasonImgStream = await seasonImgResp.Content.ReadAsStreamAsync();
                                                using var seasonImgDoc = await JsonDocument.ParseAsync(seasonImgStream);

                                                // Try to get backdrop from season posters
                                                if (seasonImgDoc.RootElement.TryGetProperty("posters", out var posters) &&
                                                    posters.ValueKind == JsonValueKind.Array &&
                                                    posters.GetArrayLength() > 0)
                                                {
                                                    var firstPoster = posters[0];
                                                    if (firstPoster.TryGetProperty("file_path", out var fp) &&
                                                        fp.ValueKind == JsonValueKind.String)
                                                    {
                                                        string? posterPath = fp.GetString();
                                                        if (!string.IsNullOrWhiteSpace(posterPath))
                                                        {
                                                            // Note: Season images are typically posters, not backdrops
                                                            // We keep the series backdrop but log that we found a season match
                                                            Logger.LogInformation(
                                                                "Found matching season {SeasonNumber} for year {Year} in '{Title}'",
                                                                seasonNumber, year, title);
                                                        }
                                                    }
                                                }
                                            }
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning(ex, "Failed to fetch season details for TV ID {MediaId}", mediaId);
                        }
                    }
                }

                Logger.LogInformation("Found TMDB {MediaType} info for '{Title}' (ID: {MediaId})", mediaType, title, mediaId);

                return new TMDBAnimeInfo
                {
                    TMDBID = mediaId.ToString(),
                    EnglishTitle = enTitle,
                    ChineseTitle = zhTitle,
                    EnglishSummary = string.IsNullOrWhiteSpace(enOverview) ? "No English summary available." : enOverview,
                    ChineseSummary = zhOverview ?? "",
                    BackdropUrl = backdropUrl,
                    OriSiteUrl = mediaId > 0 ? $"https://www.themoviedb.org/{mediaType}/{mediaId}" : ""
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

        #region Private Helper Methods

        /// <summary>
        /// Extracts year from airDate string (format: YYYY-MM-DD)
        /// </summary>
        private static int? ExtractYear(string? airDate)
        {
            if (!string.IsNullOrEmpty(airDate) && airDate.Length >= 4)
            {
                if (int.TryParse(airDate.Substring(0, 4), out var year))
                    return year;
            }
            return null;
        }

        /// <summary>
        /// Executes a TMDB search and returns the best anime match as a cloned JsonElement.
        /// First searches TV shows, then falls back to movies if no results found.
        /// </summary>
        private async Task<JsonElement?> SearchTMDBAsync(string query, int? year)
        {
            // First try TV search
            var result = await SearchTMDBByTypeAsync(query, year, "tv");
            if (result != null)
                return result;

            // Fallback to movie search (for theatrical anime films)
            Logger.LogInformation("No TV results for '{Query}', trying movie search", query);
            return await SearchTMDBByTypeAsync(query, year, "movie");
        }

        /// <summary>
        /// Executes a TMDB search for a specific media type (tv or movie)
        /// </summary>
        private async Task<JsonElement?> SearchTMDBByTypeAsync(string query, int? year, string mediaType)
        {
            const string language = "en-US";
            var url = $"search/{mediaType}?query={Uri.EscapeDataString(query)}&language={language}&page=1&include_adult=false";

            if (year.HasValue)
            {
                // TV uses first_air_date_year, movies use primary_release_year
                var yearParam = mediaType == "tv" ? "first_air_date_year" : "primary_release_year";
                url += $"&{yearParam}={year}";
                Logger.LogInformation("Searching TMDB {MediaType} for '{Query}' with year filter: {Year}", mediaType, query, year);
            }
            else
            {
                Logger.LogInformation("Searching TMDB {MediaType} for '{Query}' without year filter", mediaType, query);
            }

            Logger.LogDebug("TMDB search URL: {Url}", url);

            using var resp = await HttpClient.GetAsync(url);
            resp.EnsureSuccessStatusCode();

            using var stream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);

            if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            {
                Logger.LogInformation("TMDB {MediaType} search returned 0 results for query: '{Query}'", mediaType, query);
                return null;
            }

            Logger.LogInformation("TMDB {MediaType} search returned {Count} results for query: '{Query}'", mediaType, results.GetArrayLength(), query);

            var bestMatch = FindBestAnimeMatch(results, query);

            // Clone the JsonElement to avoid reference to disposed JsonDocument
            if (bestMatch.HasValue)
            {
                return JsonSerializer.Deserialize<JsonElement>(bestMatch.Value.GetRawText());
            }

            return null;
        }

        /// <summary>
        /// Finds the best anime match from search results.
        /// Priority: Animation+JP > Animation > First result
        /// </summary>
        private JsonElement? FindBestAnimeMatch(JsonElement results, string title)
        {
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
            if (bestMatch == null)
            {
                Logger.LogInformation("No anime match found for '{Title}', using first result", title);
                return results[0];
            }

            return bestMatch;
        }

        #endregion
    }
}
