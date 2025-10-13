using System.Net.Http;
using System.Text;
using System.Text.Json;

public sealed class AniListClient : IDisposable
{
    private const string Endpoint = "https://graphql.anilist.co";
    private static readonly JsonSerializerOptions JsonOpt = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _http;
    private readonly bool _shouldDisposeHttpClient;

    public class AnilistAnimeInfo
    {
        public string AnilistId { get; set; } = string.Empty;
        public string EnglishSummary { get; set; } = string.Empty;
        public string EnglishTitle { get; set; } = string.Empty;
        public string CoverUrl { get; set; } = string.Empty;
        public string OriSiteUrl { get; set; } = string.Empty;
    }

    public AniListClient(HttpClient? httpClient = null)
    {
        if (httpClient != null)
        {
            _http = httpClient;
            _shouldDisposeHttpClient = false;
        }
        else
        {
            _http = new HttpClient();
            _shouldDisposeHttpClient = true;
        }

        _http.DefaultRequestHeaders.Add("Accept", "application/json");
        _http.DefaultRequestHeaders.Add("User-Agent", "Anime-Sub");
    }

    /// <summary>
    /// Use Japanese Title to search
    /// </summary>
public async Task<AnilistAnimeInfo?> GetAnimeInfoAsync(string japaneseTitle)
{
    try
    {
        using var http = new HttpClient();
        
        const string Query = @"
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
            query = Query,
            variables = new { search = japaneseTitle }
        };

        var req = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json"
        );

        using var resp = await http.PostAsync(Endpoint, req);
        
        // 检查HTTP响应状态
        if (!resp.IsSuccessStatusCode)
        {
            throw new Exception($"AniList API Error: HTTP {resp.StatusCode} - {resp.ReasonPhrase} for title: {japaneseTitle}");
        }

        using var stream = await resp.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);

        // 检查GraphQL响应格式
        if (!doc.RootElement.TryGetProperty("data", out var dataElement))
        {
            throw new Exception($"AniList API Error: Invalid response format (missing 'data') for title: {japaneseTitle}");
        }

        var media = dataElement.GetProperty("Media");
        
        if (media.ValueKind == JsonValueKind.Null)
            return null;

        return new AnilistAnimeInfo
        {
            EnglishTitle   = media.GetProperty("title").GetProperty("english").GetString() ?? string.Empty,
            EnglishSummary = media.GetProperty("description").GetString() ?? string.Empty,
            CoverUrl       = media.GetProperty("coverImage").GetProperty("extraLarge").GetString() ?? string.Empty,
            OriSiteUrl     = media.GetProperty("siteUrl").GetString() ?? string.Empty
        };
    }
    catch (HttpRequestException ex)
    {
        throw new Exception($"AniList Network Error for title '{japaneseTitle}': {ex.Message}", ex);
    }
    catch (JsonException ex)
    {
        throw new Exception($"AniList JSON Parse Error for title '{japaneseTitle}': {ex.Message}", ex);
    }
    catch (KeyNotFoundException ex)
    {
        throw new Exception($"AniList Data Structure Error for title '{japaneseTitle}': Missing expected property - {ex.Message}", ex);
    }
    catch (Exception ex) when (!(ex.Message.StartsWith("AniList")))
    {
        throw new Exception($"AniList Unexpected Error for title '{japaneseTitle}': {ex.Message}", ex);
    }
}


    public void Dispose()
    {
        if (_shouldDisposeHttpClient)
        {
            _http?.Dispose();
        }
    }
}