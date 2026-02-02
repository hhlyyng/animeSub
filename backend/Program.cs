global using System.Text.Json;
global using System.Net.Http;
using Serilog;
using backend.Services.Interfaces;
using backend.Services.Implementations;
using backend.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog with enhanced settings
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", Serilog.Events.LogEventLevel.Information)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName)
    .Enrich.WithProperty("Application", "AnimeSubscription")
    .Enrich.WithMachineName()
    .Enrich.WithThreadId()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.File(
        path: "logs/app-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

// Add controllers
builder.Services.AddControllers();

// Register HttpClient factories - using factory delegates to avoid casting issues
builder.Services.AddHttpClient("bangumi-client")
    .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(30));

builder.Services.AddHttpClient("tmdb-client")
    .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(30));

builder.Services.AddHttpClient("anilist-client")
    .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(30));

builder.Services.AddHttpClient("qbittorrent-client")
    .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(30));

// Register clients via factory functions with fully qualified names
builder.Services.AddScoped<IBangumiClient>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var logger = sp.GetRequiredService<ILogger<backend.Services.Implementations.BangumiClient>>();
    return new backend.Services.Implementations.BangumiClient(factory.CreateClient("bangumi-client"), logger);
});

builder.Services.AddScoped<ITMDBClient>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var logger = sp.GetRequiredService<ILogger<backend.Services.Implementations.TMDBClient>>();
    return new backend.Services.Implementations.TMDBClient(factory.CreateClient("tmdb-client"), logger);
});

builder.Services.AddScoped<IAniListClient>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var logger = sp.GetRequiredService<ILogger<backend.Services.Implementations.AniListClient>>();
    return new backend.Services.Implementations.AniListClient(factory.CreateClient("anilist-client"), logger);
});

builder.Services.AddScoped<IQBittorrentService>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var logger = sp.GetRequiredService<ILogger<backend.Services.Implementations.QBittorrentService>>();
    return new backend.Services.Implementations.QBittorrentService(factory.CreateClient("qbittorrent-client"), logger);
});

// Register aggregation service
builder.Services.AddScoped<IAnimeAggregationService, AnimeAggregationService>();

// Register validators
builder.Services.AddScoped<backend.Services.Validators.TokenValidator>();

// Register health check service
builder.Services.AddSingleton<backend.Services.HealthCheckService>();

// Add memory cache
builder.Services.AddMemoryCache();

// Add response compression
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
});

// Add CORS (allow frontend)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Use middleware - ORDER MATTERS!
// 1. Correlation ID (must be first to track all requests)
app.UseCorrelationId();

// 2. Performance monitoring
app.UsePerformanceMonitoring();

// 3. Request/Response logging (after correlation ID)
app.UseRequestResponseLogging();

// 4. Exception handling (catch exceptions from all subsequent middleware)
app.UseGlobalExceptionHandler();

// 5. Response compression
app.UseResponseCompression();

// 6. CORS
app.UseCors("AllowFrontend");

// Map controllers
app.MapControllers();

// Health check endpoints
app.MapGet("/", (backend.Services.HealthCheckService healthCheck) =>
{
    return Results.Ok(new
    {
        status = "running",
        timestamp = DateTime.UtcNow,
        message = "Anime Subscription API",
        endpoints = new[]
        {
            "GET /api/anime/today - Get today's anime schedule",
            "GET /health - Comprehensive health check",
            "GET /health/live - Liveness probe",
            "GET /health/ready - Readiness probe"
        }
    });
});

app.MapGet("/health", (backend.Services.HealthCheckService healthCheck) =>
{
    var health = healthCheck.GetHealthStatus();
    return Results.Ok(health);
});

app.MapGet("/health/live", (backend.Services.HealthCheckService healthCheck) =>
{
    var status = healthCheck.GetLivenessStatus();
    return Results.Ok(status);
});

app.MapGet("/health/ready", (backend.Services.HealthCheckService healthCheck) =>
{
    var status = healthCheck.GetReadinessStatus();
    return Results.Ok(status);
});

try
{
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}