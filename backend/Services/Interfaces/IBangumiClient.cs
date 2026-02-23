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
    /// Get episodes for a subject. Bangumi v0 returns both season episode number (ep) and absolute sort index (sort).
    /// </summary>
    /// <param name="subjectId">Bangumi subject ID</param>
    /// <param name="limit">Page size</param>
    /// <param name="offset">Page offset</param>
    /// <returns>JsonElement containing episode list data</returns>
    Task<JsonElement> GetSubjectEpisodesAsync(int subjectId, int limit = 100, int offset = 0);

    /// <summary>
    /// Get related subjects for a subject (e.g. prequel/sequel).
    /// </summary>
    /// <param name="subjectId">Bangumi subject ID</param>
    /// <returns>JsonElement containing related subject list</returns>
    Task<JsonElement> GetSubjectRelationsAsync(int subjectId);

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
    /// Search anime by title and return all matching subjects as a list
    /// </summary>
    /// <param name="title">Title to search for</param>
    /// <returns>JsonElement array of matching subjects, or empty array if not found</returns>
    Task<JsonElement> SearchSubjectListAsync(string title);

    /// <summary>
    /// Set the API authentication token
    /// </summary>
    void SetToken(string token);
}
