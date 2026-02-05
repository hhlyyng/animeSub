namespace backend.Services.Interfaces;

/// <summary>
/// Bangumi API client for fetching anime schedule and ratings
/// </summary>
public interface IBangumiClient
{
    /// <summary>
    /// Get today's anime broadcast schedule from Bangumi
    /// </summary>
    /// <returns>JsonElement containing today's anime list</returns>
    Task<JsonElement> GetDailyBroadcastAsync();

    /// <summary>
    /// Get the count of anime broadcasting today
    /// </summary>
    /// <returns>Number of anime today, or -1 if error occurs</returns>
    Task<int> GetDailyBroadcastNumberAsync();

    /// <summary>
    /// Get detailed subject information including summary
    /// </summary>
    /// <param name="subjectId">Bangumi subject ID</param>
    /// <returns>JsonElement containing subject details with summary</returns>
    Task<JsonElement> GetSubjectDetailAsync(int subjectId);

    /// <summary>
    /// Get the full weekly calendar with all days' anime
    /// </summary>
    /// <returns>JsonElement containing the entire week's schedule</returns>
    Task<JsonElement> GetFullCalendarAsync();

    /// <summary>
    /// Search for top-ranked anime subjects
    /// </summary>
    /// <param name="limit">Number of subjects to retrieve</param>
    /// <returns>JsonElement containing ranked anime list</returns>
    Task<JsonElement> SearchTopSubjectsAsync(int limit = 10);

    /// <summary>
    /// Search anime by title keyword
    /// </summary>
    /// <param name="title">Title to search for</param>
    /// <returns>JsonElement containing search results, or default if not found</returns>
    Task<JsonElement> SearchByTitleAsync(string title);

    /// <summary>
    /// Set the API authentication token
    /// </summary>
    void SetToken(string token);
}
