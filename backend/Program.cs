global using System.Text.Json;
global using System.Net.Http;
using Serilog;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using backend.Data;
using backend.Models.Configuration;
using backend.Services;
using backend.Services.Interfaces;
using backend.Services.Implementations;
using backend.Services.Repositories;
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

// Bind API configuration from appsettings.json
builder.Services.Configure<ApiConfiguration>(
    builder.Configuration.GetSection(ApiConfiguration.SectionName));

// Bind Mikan and QBittorrent configuration
builder.Services.Configure<backend.Models.Configuration.MikanConfiguration>(
    builder.Configuration.GetSection(backend.Models.Configuration.MikanConfiguration.SectionName));
builder.Services.Configure<backend.Models.Configuration.QBittorrentConfiguration>(
    builder.Configuration.GetSection(backend.Models.Configuration.QBittorrentConfiguration.SectionName));

// Bind PreFetch configuration
builder.Services.Configure<PreFetchConfig>(
    builder.Configuration.GetSection(PreFetchConfig.SectionName));

// Add controllers
builder.Services.AddControllers();

// Configure SQLite database for anime caching
var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "Data");
Directory.CreateDirectory(dataDirectory);  // Ensure directory exists

builder.Services.AddDbContext<AnimeDbContext>(options =>
    options.UseSqlite($"Data Source={Path.Combine(dataDirectory, "anime.db")}"));

// Configure Data Protection for encrypted token storage
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(
        Path.Combine(builder.Environment.ContentRootPath, ".keys")))
    .SetApplicationName("AnimeSubscription");

// Register HttpClient factories - using factory delegates to avoid casting issues
builder.Services.AddHttpClient("bangumi-client")
    .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(30));

builder.Services.AddHttpClient("tmdb-client")
    .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(30));

builder.Services.AddHttpClient("anilist-client")
    .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(30));

builder.Services.AddHttpClient("qbittorrent-client")
    .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(30));

builder.Services.AddHttpClient("mikan-client")
    .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(30));

builder.Services.AddHttpClient("jikan-client")
    .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(30));

// Register clients via factory functions with configuration injection
builder.Services.AddScoped<IBangumiClient>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var logger = sp.GetRequiredService<ILogger<backend.Services.Implementations.BangumiClient>>();
    var config = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ApiConfiguration>>();
    return new backend.Services.Implementations.BangumiClient(factory.CreateClient("bangumi-client"), logger, config);
});

builder.Services.AddScoped<ITMDBClient>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var logger = sp.GetRequiredService<ILogger<backend.Services.Implementations.TMDBClient>>();
    var config = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ApiConfiguration>>();
    return new backend.Services.Implementations.TMDBClient(factory.CreateClient("tmdb-client"), logger, config);
});

builder.Services.AddScoped<IAniListClient>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var logger = sp.GetRequiredService<ILogger<backend.Services.Implementations.AniListClient>>();
    var config = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ApiConfiguration>>();
    return new backend.Services.Implementations.AniListClient(factory.CreateClient("anilist-client"), logger, config);
});

builder.Services.AddScoped<IQBittorrentService>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var logger = sp.GetRequiredService<ILogger<backend.Services.Implementations.QBittorrentService>>();
    var config = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<backend.Models.Configuration.QBittorrentConfiguration>>();
    return new backend.Services.Implementations.QBittorrentService(factory.CreateClient("qbittorrent-client"), logger, config);
});

builder.Services.AddScoped<IMikanClient>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var logger = sp.GetRequiredService<ILogger<backend.Services.Implementations.MikanClient>>();
    var config = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<backend.Models.Configuration.MikanConfiguration>>();
    return new backend.Services.Implementations.MikanClient(factory.CreateClient("mikan-client"), logger, config);
});

builder.Services.AddScoped<IJikanClient>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var logger = sp.GetRequiredService<ILogger<backend.Services.Implementations.JikanClient>>();
    var config = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ApiConfiguration>>();
    return new backend.Services.Implementations.JikanClient(factory.CreateClient("jikan-client"), logger, config);
});

// Register aggregation service
builder.Services.AddScoped<IAnimeAggregationService, AnimeAggregationService>();

// Register validators
builder.Services.AddScoped<backend.Services.Validators.TokenValidator>();

// Register token storage service (encrypted persistent storage)
builder.Services.AddSingleton<ITokenStorageService, TokenStorageService>();

// Register anime caching services
builder.Services.AddScoped<IAnimeRepository, AnimeRepository>();
builder.Services.AddScoped<IAnimeCacheService, AnimeCacheService>();

// Register subscription services
builder.Services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();

// Register background services
builder.Services.AddHostedService<backend.Services.Background.RssPollingService>();
builder.Services.AddHostedService<backend.Services.Background.AnimePreFetchService>();

// Register resilience service (Polly retry policies)
builder.Services.AddSingleton<IResilienceService, ResilienceService>();

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

// Initialize database (create tables if not exist)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AnimeDbContext>();
    db.Database.EnsureCreated();
    Log.Information("Database initialized: {DbPath}", db.Database.GetConnectionString());
}

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
            "GET /api/subscription - Get all subscriptions",
            "POST /api/subscription - Create subscription",
            "POST /api/subscription/{id}/check - Manual RSS check",
            "POST /api/subscription/check-all - Check all subscriptions",
            "GET /health - Comprehensive health check",
            "GET /health/live - Liveness probe",
            "GET /health/ready - Readiness probe"
        }
    });
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