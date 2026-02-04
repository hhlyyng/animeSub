namespace backend.Models;

/// <summary>
/// Status information for the pre-fetch service
/// </summary>
public class PreFetchStatus
{
    /// <summary>
    /// Whether pre-fetch is currently enabled
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Whether a pre-fetch is currently running
    /// </summary>
    public bool IsRunning { get; set; }

    /// <summary>
    /// Timestamp of the last successful pre-fetch
    /// </summary>
    public DateTime? LastRunTime { get; set; }

    /// <summary>
    /// Duration of the last pre-fetch run
    /// </summary>
    public TimeSpan? LastRunDuration { get; set; }

    /// <summary>
    /// Number of anime processed in the last run
    /// </summary>
    public int? LastRunAnimeCount { get; set; }

    /// <summary>
    /// Number of anime successfully enriched in the last run
    /// </summary>
    public int? LastRunSuccessCount { get; set; }

    /// <summary>
    /// Error message if the last run failed
    /// </summary>
    public string? LastRunError { get; set; }

    /// <summary>
    /// Scheduled hour for automatic pre-fetch (0-23)
    /// </summary>
    public int ScheduleHour { get; set; }

    /// <summary>
    /// Next scheduled pre-fetch time
    /// </summary>
    public DateTime? NextScheduledRun { get; set; }
}
