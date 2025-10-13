using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AnimeController : ControllerBase
    {
        [HttpGet("today")]
        public async Task<IActionResult> GetTodayAnime()
        {
            Console.WriteLine("API endpoint hit!");
            try
            {
                // 从请求头获取必需的 tokens
                var bangumiToken = Request.Headers["X-Bangumi-Token"].FirstOrDefault();
                var tmdbToken = Request.Headers["X-TMDB-Token"].FirstOrDefault();

                // Bangumi token 是必需的
                if (string.IsNullOrEmpty(bangumiToken))
                {
                    Console.WriteLine("BangumiToken is Empty");
                    return BadRequest(new
                    {
                        success = false,
                        message = "Bangumi token is required",
                        error_code = "MISSING_BANGUMI_TOKEN"
                    });
                }

                // 创建客户端
                using var bangumiClient = new BangumiClient(bangumiToken);
                using var tmdbClient = !string.IsNullOrEmpty(tmdbToken) ? new TMDB(tmdbToken) : null;
                using var anilistClient = new AniListClient();

                // 获取 Bangumi 今日数据
                var bangumiData = await bangumiClient.GetDailyBroadcastAsync();
                Console.WriteLine("Get Bangumi Data");
                var enrichedAnimes = new List<object>();

                foreach (var anime in bangumiData.EnumerateArray())
                {
                    var bangumiId = anime.GetProperty("id").GetInt32();

                    var OriTitle = anime.TryGetProperty("name", out var nameProperty) && nameProperty.ValueKind != JsonValueKind.Null
                        ? nameProperty.GetString() ?? ""
                        : "";

                    bool containsJapaneseInOriTitle = !string.IsNullOrEmpty(OriTitle) && 
                        System.Text.RegularExpressions.Regex.IsMatch(OriTitle, @"[\p{IsHiragana}\p{IsKatakana}]");

                    bool containsPureChineseInOriTitle = !string.IsNullOrEmpty(OriTitle) &&
                        System.Text.RegularExpressions.Regex.IsMatch(OriTitle, @"^[\p{IsCJKUnifiedIdeographs}]+$") &&
                        !System.Text.RegularExpressions.Regex.IsMatch(OriTitle, @"[\p{IsHiragana}\p{IsKatakana}]");

                    var chTitle = anime.TryGetProperty("name_cn", out var nameCn) && nameCn.ValueKind != JsonValueKind.Null
                        ? nameCn.GetString() ?? ""
                        : "";

                    Console.WriteLine($"Processing {OriTitle}");

                    var chDesc = anime.TryGetProperty("summary", out var summary) && summary.ValueKind != JsonValueKind.Null
                        ? summary.GetString() ?? ""
                        : "";

                    var score = anime.TryGetProperty("rating", out var rating) && rating.ValueKind != JsonValueKind.Null &&
                            rating.TryGetProperty("score", out var scoreEl) && scoreEl.ValueKind != JsonValueKind.Null
                            ? scoreEl.GetDouble().ToString("F1")
                            : "0";

                    // 并行获取外部数据（可选）
                    // 安全地获取外部数据
                    TMDB.TMDBAnimeInfo? tmdbResult = null;
                    AniListClient.AnilistAnimeInfo? anilistResult = null;

                if (!string.IsNullOrWhiteSpace(OriTitle))
                {
                    var tmdbTask = Task.Run(async () =>
                    {
                        try
                        {
                            Console.WriteLine($"Calling TMDB API for '{OriTitle}'...");
                            var result = await (tmdbClient?.GetAnimeSummaryAndBackdropAsync(OriTitle)
                                        ?? Task.FromResult<TMDB.TMDBAnimeInfo?>(null));
                            Console.WriteLine($"TMDB API completed for '{OriTitle}'");
                            return result;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"TMDB API failed for '{OriTitle}': {ex.Message}");
                            Console.WriteLine($"Stack trace: {ex.StackTrace}");
                            return null;
                        }
                    });
                    
                    var anilistTask = Task.Run(async () =>
                    {
                        try
                        {
                            Console.WriteLine($"Calling AniList API for '{OriTitle}'...");
                            var result = await anilistClient.GetAnimeInfoAsync(OriTitle);
                            Console.WriteLine($"AniList API completed for '{OriTitle}'");
                            return result;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"AniList API failed for '{OriTitle}': {ex.Message}");
                            Console.WriteLine($"Stack trace: {ex.StackTrace}");
                            return null;
                        }
                    });
                    
                    await Task.WhenAll(tmdbTask, anilistTask);
                
                    tmdbResult = tmdbTask.Result;
                    anilistResult = anilistTask.Result;
                }

                    // 构建响应对象
                    enrichedAnimes.Add(new
                    {
                        bangumi_id = bangumiId.ToString(),
                        jp_title = containsJapaneseInOriTitle? OriTitle : "",
                        ch_title = containsPureChineseInOriTitle? OriTitle : chTitle,
                        en_title = tmdbResult?.EnglishTitle ?? anilistResult?.EnglishTitle ?? "",
                        ch_desc = chDesc,
                        en_desc = tmdbResult?.EnglishSummary ?? anilistResult?.EnglishSummary ?? "",
                        score = score,
                        images = new
                        {
                            portrait = anime.TryGetProperty("images", out var images) && images.ValueKind != JsonValueKind.Null &&
                                    images.TryGetProperty("large", out var large) && large.ValueKind != JsonValueKind.Null
                                    ? large.GetString() ?? ""
                                    : "",
                            landscape = tmdbResult?.BackdropUrl ?? ""
                        },
                        external_urls = new
                        {
                            bangumi = $"https://bgm.tv/subject/{bangumiId}",
                            tmdb = tmdbResult?.OriSiteUrl ?? "",
                            anilist = anilistResult?.OriSiteUrl ?? ""
                        }
                    });
                    Console.WriteLine($"Successfully processed: {OriTitle}");
                }
              

                return Ok(new
                {
                    success = true,
                    data = new
                    {
                        count = enrichedAnimes.Count,
                        animes = enrichedAnimes
                    },
                    message = "Success"
                });
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"{ex.Message}");
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message,
                    error_code = "INVALID_TOKEN"
                });
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"{ex.Message}");
                return StatusCode(502, new
                {
                    success = false,
                    message = "External API request failed",
                    error_code = "EXTERNAL_API_ERROR",
                    details = ex.Message
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Message}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Internal server error",
                    error_code = "INTERNAL_ERROR",
                    details = ex.Message
                });
            }
        }
    }
}