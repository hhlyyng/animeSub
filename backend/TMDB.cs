using System.Net.Http;
using System.Text.Json;

public sealed class TMDB: IDisposable
{
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "https://api.themoviedb.org/3";
    private const string ImageBaseUrl = "https://image.tmdb.org/t/p/";
    private readonly string _accessToken ;

    public class TMDBAnimeInfo
    {
        public string TMDBID { get; set; } = string.Empty;
        public string EnglishSummary { get; set; } = string.Empty;
        public string ChineseSummary { get; set; } = string.Empty;
        public string ChineseTitle { get; set; } = string.Empty;
        public string EnglishTitle { get; set; } = string.Empty;
        public string BackdropUrl { get; set; } = string.Empty;
        public string OriSiteUrl { get; set; } = string.Empty;
    }

    public TMDB(string? accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new ArgumentException("Access token cannot be null or empty.", nameof(accessToken));

        _accessToken = accessToken;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
        _httpClient.DefaultRequestHeaders.Add("accept", "application/json");
    }

    // Get configuration for available image sizes
    public async Task<TMDBAnimeInfo?> GetAnimeSummaryAndBackdropAsync(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("title cannot be null or empty.", nameof(title));

            const string language = "en-US";
            var url = $"{BaseUrl}/search/tv?query={Uri.EscapeDataString(title)}&language={language}&page=1&include_adult=false";

            using var resp = await _httpClient.GetAsync(url);
            resp.EnsureSuccessStatusCode();

            using var stream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);

            if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                    return new TMDBAnimeInfo { EnglishSummary = "No result found in TMDB.", BackdropUrl = "" };

            var first = results[0];

                // id
            int tvId = first.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var idVal) ? idVal : 0;

                // 英文标题（来自 search 的 en-US 结果，或稍后用 translations 覆盖）
            string? enTitleFromSearch = first.TryGetProperty("name", out var nm) ? nm.GetString() : null;

                // 英文简介（search 结果兜底）
            string enOverviewFromSearch = first.TryGetProperty("overview", out var ov)
                ? (ov.GetString() ?? "No English summary available in TMDB.")
                : "No English summary available.";

                // backdrop (original)
            string backdropUrl = "";
            if (first.TryGetProperty("backdrop_path", out var bp) && bp.ValueKind == JsonValueKind.String)
                {
                string? path = bp.GetString();
                if (!string.IsNullOrWhiteSpace(path))
                    backdropUrl = $"{ImageBaseUrl}original{path}";
                }

                // ---- 取多语言翻译（一次请求拿到中/英等所有翻译） ----
            string enTitle = enTitleFromSearch ?? "";
            string zhTitle = "";
            string enOverview = enOverviewFromSearch;
            string zhOverview = "";

                if (tvId > 0)
                {
                    var tUrl = $"{BaseUrl}/tv/{tvId}/translations";
                    using var tResp = await _httpClient.GetAsync(tUrl);
                    tResp.EnsureSuccessStatusCode();

                    using var tStream = await tResp.Content.ReadAsStreamAsync();
                    using var tDoc = await JsonDocument.ParseAsync(tStream);

                    if (tDoc.RootElement.TryGetProperty("translations", out var trans) && trans.ValueKind == JsonValueKind.Array)
                    {
                        // 找英文
                        var en = trans.EnumerateArray()
                                    .FirstOrDefault(x => x.TryGetProperty("iso_639_1", out var l) && l.GetString() == "en");

                        // 找中文（优先 zh-CN，其次 zh/zh-TW/zh-HK）
                        JsonElement zh = default;
                        bool hasZh = false;

                        foreach (var cand in trans.EnumerateArray())
                        {
                            if (cand.TryGetProperty("iso_639_1", out var l) && l.GetString() == "zh")
                            {
                                // 优先 CN
                                string? region = cand.TryGetProperty("iso_3166_1", out var r) ? r.GetString() : null;
                                if (region == "CN")
                                {
                                    zh = cand; hasZh = true; break;
                                }
                                // 先暂存，若没有 CN 再用其它 zh 区域
                                if (!hasZh) { zh = cand; hasZh = true; }
                            }
                        }

                        // 解析英文 data
                        if (en.ValueKind != JsonValueKind.Undefined &&
                            en.TryGetProperty("data", out var enData) && enData.ValueKind == JsonValueKind.Object)
                        {
                            if (enData.TryGetProperty("name", out var enNameEl))
                                enTitle = enNameEl.GetString() ?? enTitle;

                            if (enData.TryGetProperty("overview", out var enOvEl))
                                enOverview = enOvEl.GetString() ?? enOverview;
                        }

                        // 解析中文 data
                        if (hasZh &&
                            zh.TryGetProperty("data", out var zhData) && zhData.ValueKind == JsonValueKind.Object)
                        {
                            if (zhData.TryGetProperty("name", out var zhNameEl))
                                zhTitle = zhNameEl.GetString() ?? zhTitle;

                            if (zhData.TryGetProperty("overview", out var zhOvEl))
                                zhOverview = zhOvEl.GetString() ?? zhOverview;
                        }
                    }
                }

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
            }
    
    // Get movie images (posters, backdrops, logos)
    public async Task<List<string>> GetMovieImagesAsync(int movieId, string imageType = "posters", string size = "w500", string language = "en-US")
    {
        var url = $"{BaseUrl}/movie/{movieId}/images?language={language}&include_image_language=en,null";
        var response = await _httpClient.GetAsync(url);
        var content = await response.Content.ReadAsStringAsync();
        var data = JsonSerializer.Deserialize<JsonElement>(content);

        var imageUrls = new List<string>();
        if (data.TryGetProperty(imageType, out var images))
        {
            foreach (var image in images.EnumerateArray())
            {
                var filePath = image.GetProperty("file_path").GetString();
                imageUrls.Add($"{ImageBaseUrl}{size}{filePath}");
            }
        }

        return imageUrls;
    }

    // Get TV show images
    public async Task<List<string>> GetTVImagesAsync(int tvId, string imageType = "posters", string size = "w500", string language = "en-US")
    {
        var url = $"{BaseUrl}/tv/{tvId}/images?language={language}&include_image_language=en,null";
        var response = await _httpClient.GetAsync(url);
        var content = await response.Content.ReadAsStringAsync();
        var data = JsonSerializer.Deserialize<JsonElement>(content);
        
        var imageUrls = new List<string>();
        if (data.TryGetProperty(imageType, out var images))
        {
            foreach (var image in images.EnumerateArray())
            {
                var filePath = image.GetProperty("file_path").GetString();
                imageUrls.Add($"{ImageBaseUrl}{size}{filePath}");
            }
        }
        
        return imageUrls;
    }

    // Get person images
    public async Task<List<string>> GetPersonImagesAsync(int personId, string size = "w185")
    {
        var response = await _httpClient.GetAsync($"{BaseUrl}/person/{personId}/images");
        var content = await response.Content.ReadAsStringAsync();
        var data = JsonSerializer.Deserialize<JsonElement>(content);
        
        var profileUrls = new List<string>();
        if (data.TryGetProperty("profiles", out var profiles))
        {
            foreach (var profile in profiles.EnumerateArray())
            {
                var filePath = profile.GetProperty("file_path").GetString();
                profileUrls.Add($"{ImageBaseUrl}{size}{filePath}");
            }
        }
        
        return profileUrls;
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}


