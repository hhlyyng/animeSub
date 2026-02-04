using System.Text.Json;
using backend.Data.Entities;
using backend.Models;
using backend.Services.Interfaces;
using backend.Services.Repositories;
using Microsoft.Extensions.Options;
using backend.Models.Configuration;

namespace backend.Services.Background;

/// <summary>
/// Background service for pre-fetching anime data from external APIs
/// Runs at scheduled times (default: 3:00 AM) to populate the database
/// </summary>
public class AnimePreFetchService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AnimePreFetchService> _logger;
    private readonly PreFetchConfig _config;

    // Static status for querying from controllers
    private static readonly object _statusLock = new();
    private static PreFetchStatus _status = new();

    /// <summary>
    /// Get the current status of the pre-fetch service
    /// </summary>
    public static PreFetchStatus GetStatus()
    {
        lock (_statusLock)
        {
            return new PreFetchStatus
            {
                Enabled = _status.Enabled,
                IsRunning = _status.IsRunning,
                LastRunTime = _status.LastRunTime,
                LastRunDuration = _status.LastRunDuration,
                LastRunAnimeCount = _status.LastRunAnimeCount,
                LastRunSuccessCount = _status.LastRunSuccessCount,
                LastRunError = _status.LastRunError,
                ScheduleHour = _status.ScheduleHour,
                NextScheduledRun = _status.NextScheduledRun
            };
        }
    }

    private static void UpdateStatus(Action<PreFetchStatus> update)
    {
        lock (_statusLock)
        {
            update(_status);
        }
    }

    public AnimePreFetchService(
        IServiceScopeFactory scopeFactory,
        ILogger<AnimePreFetchService> logger,
        IOptions<PreFetchConfig> config)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _config = config.Value;

        // Initialize status
        UpdateStatus(s =>
        {
            s.Enabled = _config.Enabled;
            s.ScheduleHour = _config.ScheduleHour;
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AnimePreFetchService started. Schedule: {Hour}:00, Enabled: {Enabled}",
            _config.ScheduleHour, _config.Enabled);

        if (!_config.Enabled)
        {
            _logger.LogInformation("AnimePreFetchService is disabled, exiting");
            return;
        }

        // Run immediately on startup if configured
        if (_config.RunOnStartup)
        {
            _logger.LogInformation("Running pre-fetch on startup");
            await RunPreFetchAsync(stoppingToken);
        }

        // Schedule daily runs
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var nextRun = GetNextScheduledTime(now);
            var delay = nextRun - now;

            UpdateStatus(s => s.NextScheduledRun = nextRun);

            _logger.LogInformation("Next pre-fetch scheduled at {NextRun} (in {Delay})",
                nextRun, delay);

            try
            {
                await Task.Delay(delay, stoppingToken);
                await RunPreFetchAsync(stoppingToken);
            }
            catch (TaskCanceledException)
            {
                _logger.LogInformation("AnimePreFetchService is stopping");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in pre-fetch service, will retry at next scheduled time");
                UpdateStatus(s => s.LastRunError = ex.Message);
            }
        }
    }

    private DateTime GetNextScheduledTime(DateTime now)
    {
        var scheduledTime = new DateTime(now.Year, now.Month, now.Day, _config.ScheduleHour, 0, 0);

        // If scheduled time has passed today, schedule for tomorrow
        if (now >= scheduledTime)
        {
            scheduledTime = scheduledTime.AddDays(1);
        }

        return scheduledTime;
    }

    /// <summary>
    /// Run the pre-fetch process for the entire week's anime
    /// </summary>
    public async Task RunPreFetchAsync(CancellationToken cancellationToken = default)
    {
        // Check if already running
        lock (_statusLock)
        {
            if (_status.IsRunning)
            {
                _logger.LogWarning("Pre-fetch already in progress, skipping");
                return;
            }
            _status.IsRunning = true;
            _status.LastRunError = null;
        }

        _logger.LogInformation("Starting anime pre-fetch process");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var bangumiClient = scope.ServiceProvider.GetRequiredService<IBangumiClient>();
            var tmdbClient = scope.ServiceProvider.GetRequiredService<ITMDBClient>();
            var anilistClient = scope.ServiceProvider.GetRequiredService<IAniListClient>();
            var repository = scope.ServiceProvider.GetRequiredService<IAnimeRepository>();

            // Set tokens from configuration
            var apiConfig = scope.ServiceProvider.GetRequiredService<IOptions<ApiConfiguration>>().Value;
            bangumiClient.SetToken(_config.BangumiToken);
            tmdbClient.SetToken(_config.TmdbToken);

            // Fetch entire week's calendar
            var calendar = await FetchWeeklyCalendarAsync(bangumiClient, cancellationToken);
            if (calendar == null || calendar.Count == 0)
            {
                _logger.LogWarning("Failed to fetch weekly calendar, aborting pre-fetch");
                return;
            }

            _logger.LogInformation("Fetched {Count} total anime across all weekdays", calendar.Sum(kv => kv.Value.Count));

            // Process each day's anime
            int totalProcessed = 0;
            int totalSuccess = 0;

            foreach (var (weekday, animeList) in calendar)
            {
                _logger.LogInformation("Processing weekday {Weekday}: {Count} anime", weekday, animeList.Count);

                var enrichedAnimes = new List<AnimeInfoEntity>();

                // Process anime in parallel with concurrency limit
                var semaphore = new SemaphoreSlim(_config.MaxConcurrency);
                var tasks = animeList.Select(async anime =>
                {
                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        var enriched = await EnrichAnimeAsync(
                            anime, weekday, bangumiClient, tmdbClient, anilistClient, cancellationToken);
                        if (enriched != null)
                        {
                            lock (enrichedAnimes)
                            {
                                enrichedAnimes.Add(enriched);
                            }
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);

                // Save to database
                if (enrichedAnimes.Count > 0)
                {
                    await repository.SaveAnimeInfoBatchAsync(enrichedAnimes);
                    totalSuccess += enrichedAnimes.Count;
                }

                totalProcessed += animeList.Count;

                // Delay between days to avoid rate limiting
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }

            stopwatch.Stop();
            _logger.LogInformation(
                "Pre-fetch completed: {Success}/{Total} anime enriched in {Elapsed}",
                totalSuccess, totalProcessed, stopwatch.Elapsed);

            UpdateStatus(s =>
            {
                s.IsRunning = false;
                s.LastRunTime = DateTime.UtcNow;
                s.LastRunDuration = stopwatch.Elapsed;
                s.LastRunAnimeCount = totalProcessed;
                s.LastRunSuccessCount = totalSuccess;
                s.LastRunError = null;
            });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Pre-fetch failed after {Elapsed}", stopwatch.Elapsed);

            UpdateStatus(s =>
            {
                s.IsRunning = false;
                s.LastRunTime = DateTime.UtcNow;
                s.LastRunDuration = stopwatch.Elapsed;
                s.LastRunError = ex.Message;
            });

            throw;
        }
    }

    private async Task<Dictionary<int, List<JsonElement>>?> FetchWeeklyCalendarAsync(
        IBangumiClient bangumiClient,
        CancellationToken cancellationToken)
    {
        try
        {
            var calendarJson = await bangumiClient.GetFullCalendarAsync();
            var result = new Dictionary<int, List<JsonElement>>();

            foreach (var dayElement in calendarJson.EnumerateArray())
            {
                if (dayElement.TryGetProperty("weekday", out var weekdayProp) &&
                    weekdayProp.TryGetProperty("id", out var weekdayId) &&
                    dayElement.TryGetProperty("items", out var items))
                {
                    int weekday = weekdayId.GetInt32();
                    var animeList = items.EnumerateArray().ToList();
                    result[weekday] = animeList;
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch weekly calendar");
            return null;
        }
    }

    private async Task<AnimeInfoEntity?> EnrichAnimeAsync(
        JsonElement anime,
        int weekday,
        IBangumiClient bangumiClient,
        ITMDBClient tmdbClient,
        IAniListClient anilistClient,
        CancellationToken cancellationToken)
    {
        try
        {
            var bangumiId = anime.GetProperty("id").GetInt32();

            // Extract basic info from calendar
            var oriTitle = anime.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
            var chTitle = anime.TryGetProperty("name_cn", out var nameCnEl) ? nameCnEl.GetString() ?? "" : "";

            bool containsJapanese = !string.IsNullOrEmpty(oriTitle) &&
                System.Text.RegularExpressions.Regex.IsMatch(oriTitle, @"[\p{IsHiragana}\p{IsKatakana}]");

            var score = anime.TryGetProperty("rating", out var rating) &&
                        rating.TryGetProperty("score", out var scoreEl)
                        ? scoreEl.GetDouble().ToString("F1")
                        : "0";

            var portraitUrl = anime.TryGetProperty("images", out var images) &&
                             images.TryGetProperty("large", out var large)
                             ? large.GetString() ?? ""
                             : "";

            // Fetch subject detail for summary and air date
            string chDesc = "";
            string? airDate = null;

            try
            {
                var subjectDetail = await bangumiClient.GetSubjectDetailAsync(bangumiId);

                if (subjectDetail.TryGetProperty("summary", out var summaryEl))
                    chDesc = summaryEl.GetString() ?? "";

                if (subjectDetail.TryGetProperty("date", out var dateEl))
                    airDate = dateEl.GetString();

                if (string.IsNullOrEmpty(chTitle) &&
                    subjectDetail.TryGetProperty("name_cn", out var detailNameCn))
                    chTitle = detailNameCn.GetString() ?? "";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch subject detail for BangumiId {BangumiId}", bangumiId);
            }

            // Fetch TMDB and AniList in parallel
            var tmdbTask = tmdbClient.GetAnimeSummaryAndBackdropAsync(oriTitle, airDate);
            var anilistTask = anilistClient.GetAnimeInfoAsync(oriTitle);

            await Task.WhenAll(tmdbTask, anilistTask);

            var tmdbResult = await tmdbTask;
            var anilistResult = await anilistTask;

            // Determine final values with fallback logic
            var jpTitle = containsJapanese ? oriTitle : "";
            var enTitle = tmdbResult?.EnglishTitle ?? anilistResult?.EnglishTitle ?? "";

            // Check if Bangumi description is Japanese
            bool bangumiDescIsJapanese = !string.IsNullOrEmpty(chDesc) &&
                System.Text.RegularExpressions.Regex.IsMatch(chDesc, @"[\p{IsHiragana}\p{IsKatakana}]");

            string finalChDesc;
            if (!string.IsNullOrEmpty(chDesc) && !bangumiDescIsJapanese)
                finalChDesc = chDesc;
            else if (!string.IsNullOrEmpty(tmdbResult?.ChineseSummary))
                finalChDesc = tmdbResult.ChineseSummary;
            else
                finalChDesc = "无可用中文介绍";

            string finalEnDesc;
            if (!string.IsNullOrEmpty(tmdbResult?.EnglishSummary) &&
                tmdbResult.EnglishSummary != "No English summary available." &&
                tmdbResult.EnglishSummary != "No result found in TMDB.")
                finalEnDesc = tmdbResult.EnglishSummary;
            else if (!string.IsNullOrEmpty(anilistResult?.EnglishSummary))
                finalEnDesc = anilistResult.EnglishSummary;
            else
                finalEnDesc = "No English description available";

            // Skip if no valid title
            if (string.IsNullOrEmpty(jpTitle) && string.IsNullOrEmpty(chTitle) && string.IsNullOrEmpty(enTitle))
            {
                _logger.LogWarning("Skipping anime BangumiId {BangumiId} - no valid title", bangumiId);
                return null;
            }

            return new AnimeInfoEntity
            {
                BangumiId = bangumiId,
                NameJapanese = jpTitle,
                NameChinese = chTitle,
                NameEnglish = enTitle,
                DescChinese = finalChDesc,
                DescEnglish = finalEnDesc,
                Score = score,
                ImagePortrait = portraitUrl,
                ImageLandscape = tmdbResult?.BackdropUrl ?? "",
                TmdbId = int.TryParse(tmdbResult?.TMDBID, out var tmdbId) ? tmdbId : null,
                AnilistId = int.TryParse(anilistResult?.AnilistId, out var anilistId) ? anilistId : null,
                UrlBangumi = $"https://bgm.tv/subject/{bangumiId}",
                UrlTmdb = tmdbResult?.OriSiteUrl ?? "",
                UrlAnilist = anilistResult?.OriSiteUrl ?? "",
                AirDate = airDate,
                Weekday = weekday,
                IsPreFetched = true
            };
        }
        catch (Exception ex)
        {
            var bangumiId = anime.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0;
            _logger.LogError(ex, "Failed to enrich anime BangumiId {BangumiId}", bangumiId);
            return null;
        }
    }
}
