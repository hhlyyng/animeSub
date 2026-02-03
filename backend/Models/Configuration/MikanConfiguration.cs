namespace backend.Models.Configuration;

/// <summary>
/// Configuration for Mikan RSS service
/// </summary>
public class MikanConfiguration
{
    public const string SectionName = "Mikan";

    /// <summary>
    /// Base URL for Mikan (default: https://mikanani.me)
    /// Can be changed to mirror sites if needed
    /// </summary>
    public string BaseUrl { get; set; } = "https://mikanani.me";

    /// <summary>
    /// Polling interval in minutes (default: 30)
    /// Minimum recommended value: 5 minutes
    /// </summary>
    public int PollingIntervalMinutes { get; set; } = 30;

    /// <summary>
    /// Whether to enable automatic RSS polling (default: true)
    /// </summary>
    public bool EnablePolling { get; set; } = true;

    /// <summary>
    /// Maximum number of subscriptions to check per poll cycle (default: 50)
    /// </summary>
    public int MaxSubscriptionsPerPoll { get; set; } = 50;

    /// <summary>
    /// HTTP request timeout in seconds (default: 30)
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Delay in seconds before starting the first poll after service startup (default: 30)
    /// Allows other services to initialize
    /// </summary>
    public int StartupDelaySeconds { get; set; } = 30;
}
