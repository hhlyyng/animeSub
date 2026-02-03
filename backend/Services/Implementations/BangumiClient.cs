using System.Text.Json;
using Microsoft.Extensions.Options;
using backend.Models.Configuration;
using backend.Services.Interfaces;

namespace backend.Services.Implementations
{
    /// <summary>
    /// Bangumi API client implementation for fetching anime schedule and ratings
    /// </summary>
    public class BangumiClient : ApiClientBase<BangumiClient>, IBangumiClient
    {
        public BangumiClient(
            HttpClient httpClient,
            ILogger<BangumiClient> logger,
            IOptions<ApiConfiguration> config)
            : base(httpClient, logger, config.Value.Bangumi.BaseUrl)
        {
            HttpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            HttpClient.DefaultRequestHeaders.Add("User-Agent", "Anime-Sub (https://github.com/hhlyyng/anime-subscription)");
            HttpClient.Timeout = TimeSpan.FromSeconds(config.Value.Bangumi.TimeoutSeconds);
        }

        public Task<JsonElement> GetDailyBroadcastAsync() =>
            ExecuteAsync(async () =>
            {
                EnsureTokenSet();

                var response = await HttpClient.GetAsync("calendar");
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var fullCalendar = JsonDocument.Parse(content).RootElement;

                int todayId = (int)DateTime.Now.DayOfWeek;
                if (todayId == 0) todayId = 7;

                foreach (var dayElement in fullCalendar.EnumerateArray())
                {
                    var weekday = dayElement.GetProperty("weekday");
                    int weekdayId = weekday.GetProperty("id").GetInt32();

                    if (weekdayId == todayId)
                    {
                        var items = dayElement.GetProperty("items");
                        Logger.LogInformation("Retrieved {Count} anime for weekday {WeekdayId}",
                            items.EnumerateArray().Count(), todayId);
                        return items;
                    }
                }

                throw new InvalidOperationException("Today's data not found in calendar response");
            }, "GetDailyBroadcast");

        public async Task<int> GetDailyBroadcastNumberAsync()
        {
            try
            {
                var todayBroadcast = await GetDailyBroadcastAsync();
                int count = todayBroadcast.EnumerateArray().Count();
                Logger.LogInformation("Daily broadcast count: {Count}", count);
                return count;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error getting daily broadcast number");
                return -1;
            }
        }
    }
}
