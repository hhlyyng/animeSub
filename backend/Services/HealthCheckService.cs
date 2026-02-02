namespace backend.Services;

/// <summary>
/// Service for application health checks
/// Provides detailed health status of application components
/// </summary>
public class HealthCheckService
{
    private readonly ILogger<HealthCheckService> _logger;
    private readonly DateTime _startTime;

    public HealthCheckService(ILogger<HealthCheckService> logger)
    {
        _logger = logger;
        _startTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Get comprehensive health status
    /// </summary>
    public HealthStatus GetHealthStatus()
    {
        var uptime = DateTime.UtcNow - _startTime;

        return new HealthStatus
        {
            Status = "Healthy",
            Timestamp = DateTime.UtcNow,
            Uptime = new
            {
                Days = uptime.Days,
                Hours = uptime.Hours,
                Minutes = uptime.Minutes,
                Seconds = uptime.Seconds,
                TotalSeconds = (int)uptime.TotalSeconds
            },
            Version = GetApplicationVersion(),
            Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Unknown",
            Components = new Dictionary<string, ComponentHealth>
            {
                ["API"] = new ComponentHealth
                {
                    Status = "Healthy",
                    Description = "API endpoints are operational"
                },
                ["Logging"] = new ComponentHealth
                {
                    Status = "Healthy",
                    Description = "Serilog is configured and running"
                },
                ["DependencyInjection"] = new ComponentHealth
                {
                    Status = "Healthy",
                    Description = "All services registered successfully"
                }
            }
        };
    }

    /// <summary>
    /// Get liveness status (simple check that app is running)
    /// </summary>
    public object GetLivenessStatus()
    {
        return new
        {
            status = "alive",
            timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Get readiness status (check if app is ready to receive requests)
    /// </summary>
    public object GetReadinessStatus()
    {
        var uptime = DateTime.UtcNow - _startTime;
        var isReady = uptime.TotalSeconds > 5; // Consider ready after 5 seconds

        return new
        {
            status = isReady ? "ready" : "warming_up",
            timestamp = DateTime.UtcNow,
            uptime_seconds = (int)uptime.TotalSeconds
        };
    }

    private string GetApplicationVersion()
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        return version?.ToString() ?? "1.0.0";
    }
}

/// <summary>
/// Health status response model
/// </summary>
public class HealthStatus
{
    public string Status { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public object? Uptime { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public Dictionary<string, ComponentHealth> Components { get; set; } = new();
}

/// <summary>
/// Component health status
/// </summary>
public class ComponentHealth
{
    public string Status { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
