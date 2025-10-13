using System.Net.Http;
using System.Text.Json;

public sealed class BangumiClient : IDisposable
{
    private const string Endpoint = "https://api.bgm.tv";
    private static readonly JsonSerializerOptions JsonOpt = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _httpClient;
    private readonly bool _shouldDisposeHttpClient;
    private readonly string _accessToken;

    public BangumiClient(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new ArgumentException("Access token cannot be null or empty.", nameof(accessToken));
        }

        _accessToken = accessToken;
        _httpClient = new HttpClient();
        _shouldDisposeHttpClient = true;

        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");
        _httpClient.DefaultRequestHeaders.Add("accept", "application/json");
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Anime-Sub (https://github.com/hhlyyng/anime-subscription)");
    }

    public async Task<JsonElement> GetDailyBroadcastAsync()
    {
        var response = await _httpClient.GetAsync($"{Endpoint}/calendar");

        if (response.IsSuccessStatusCode) //200
        {
            var content = await response.Content.ReadAsStringAsync();
            var fullCalender = JsonDocument.Parse(content).RootElement;

            int todayId = (int)DateTime.Now.DayOfWeek;
            if (todayId == 0) todayId = 7;

            foreach (var dayElement in fullCalender.EnumerateArray())
            {
                var weekday = dayElement.GetProperty("weekday");
                int weekdayId = weekday.GetProperty("id").GetInt32();

                if (weekdayId == todayId)
                {
                    return dayElement.GetProperty("items");
                }
            }
            
            throw new InvalidOperationException($"Today's data not found in calendar response");
        }
        else
        {
            throw new HttpRequestException($"Bangumi API request failed with status code: {response.StatusCode}");
        }
    }

    public async Task<int> GetDailyBroadcastNumberAsync()
    {
        try
        {
            var todayBroadcast = await GetDailyBroadcastAsync();
            return todayBroadcast.EnumerateArray().Count();
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"Internet Connection Error: {ex.Message}");
            return -1; // -1 indicates failure
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"Data fetching error in JSON: {ex.Message}");
            return -1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unknown Mistake: {ex.Message}");
            return -1;
        }
    }

    public void Dispose()
    {
        if (_shouldDisposeHttpClient)
        {
            _httpClient?.Dispose();
        }
    }

}