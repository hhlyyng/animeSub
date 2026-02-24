global using System.Text.Json;
global using System.Net.Http;
using System.Text;
using Serilog;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using backend.Data;
using backend.Models.Configuration;
using backend.Services;
using backend.Services.Interfaces;
using backend.Services.Implementations;
using backend.Services.Repositories;
using backend.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Determine data directory (Docker: set DATA_DIR env var, e.g. /app/data)
var envDataDir = builder.Configuration["DATA_DIR"];
var dataDirectory = string.IsNullOrWhiteSpace(envDataDir)
    ? Path.Combine(builder.Environment.ContentRootPath, "Data")
    : envDataDir;
Directory.CreateDirectory(dataDirectory);

// Expose resolved data dir to services (e.g. SettingsController writes runtime config here)
builder.Configuration["DataDir"] = dataDirectory;

// Runtime overrides edited from settings page
// In Docker: reads from DATA_DIR/appsettings.runtime.json; locally: from ContentRoot/appsettings.runtime.json
var runtimeConfigPath = string.IsNullOrWhiteSpace(envDataDir)
    ? "appsettings.runtime.json"
    : Path.Combine(envDataDir, "appsettings.runtime.json");
builder.Configuration.AddJsonFile(runtimeConfigPath, optional: true, reloadOnChange: true);

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
builder.Services.AddDbContext<AnimeDbContext>(options =>
    options.UseSqlite($"Data Source={Path.Combine(dataDirectory, "anime.db")}"));

// Configure Data Protection for encrypted token storage
var keysDirectory = Path.Combine(dataDirectory, ".keys");
Directory.CreateDirectory(keysDirectory);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory))
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

builder.Services.AddSingleton<IQBittorrentService>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var logger = sp.GetRequiredService<ILogger<backend.Services.Implementations.QBittorrentService>>();
    var config = sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<backend.Models.Configuration.QBittorrentConfiguration>>();
    return new backend.Services.Implementations.QBittorrentService(factory.CreateClient("qbittorrent-client"), logger, config);
});

builder.Services.AddScoped<IMikanClient>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var logger = sp.GetRequiredService<ILogger<backend.Services.Implementations.MikanClient>>();
    var config = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<backend.Models.Configuration.MikanConfiguration>>();
    var titleParser = sp.GetRequiredService<ITorrentTitleParser>();
    var dbContext = sp.GetRequiredService<AnimeDbContext>();
    return new backend.Services.Implementations.MikanClient(factory.CreateClient("mikan-client"), logger, config, titleParser, dbContext);
});

// Register torrent title parser
builder.Services.AddScoped<ITorrentTitleParser, TorrentTitleParser>();

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
builder.Services.AddHostedService<backend.Services.Background.MikanFeedSubgroupCleanupService>();
builder.Services.AddHostedService<backend.Services.Background.RssPollingService>();
builder.Services.AddHostedService<backend.Services.Background.AnimePreFetchService>();
builder.Services.AddHostedService<backend.Services.Background.DownloadProgressSyncService>();
builder.Services.AddHostedService<backend.Services.Background.AnimeTitleBackfillService>();

// Register random anime pool services
builder.Services.AddSingleton<backend.Services.Implementations.AnimePoolService>();
builder.Services.AddSingleton<backend.Services.Interfaces.IAnimePoolService>(sp =>
    sp.GetRequiredService<backend.Services.Implementations.AnimePoolService>());
builder.Services.AddHostedService<backend.Services.Implementations.AnimePoolBuilderService>();

// Register resilience service (Polly retry policies)
builder.Services.AddSingleton<IResilienceService, ResilienceService>();

// Register health check service
builder.Services.AddSingleton<backend.Services.HealthCheckService>();

// Register auth service
builder.Services.AddScoped<IAuthService, AuthService>();

// Configure JWT authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = "AnimeSub",
        ValidateAudience = true,
        ValidAudience = "AnimeSub",
        ValidateIssuerSigningKey = true,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(5)
    };

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            // Dynamically resolve the JWT secret at request time
            var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            var secret = config["Auth:JwtSecret"];
            if (!string.IsNullOrWhiteSpace(secret))
            {
                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
                context.Options.TokenValidationParameters.IssuerSigningKey = key;
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

// Add memory cache
builder.Services.AddMemoryCache();

// Add response compression
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
});

// Add CORS (allow frontend) - configure via CORS_ORIGINS env var (comma-separated, or * for any)
var corsOriginsRaw = builder.Configuration["CORS_ORIGINS"] ?? "http://localhost:5173,http://localhost:3000";
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        if (corsOriginsRaw.Trim() == "*")
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        else
            policy.WithOrigins(corsOriginsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                  .AllowAnyHeader()
                  .AllowAnyMethod();
    });
});

var app = builder.Build();

// Initialize database (create tables if not exist)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AnimeDbContext>();
    var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();

    db.Database.EnsureCreated();
    backend.Data.DbSchemaPatcher.ApplyAsync(
        db,
        loggerFactory.CreateLogger("DbSchemaPatcher")).GetAwaiter().GetResult();

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

// 7. Authentication & Authorization
app.UseAuthentication();
app.UseAuthorization();

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
            "GET /api/subscription/bangumi/{bangumiId} - Get subscription by BangumiId",
            "POST /api/subscription - Create subscription",
            "POST /api/subscription/ensure - Ensure subscription exists and enabled",
            "POST /api/subscription/{id}/cancel - Cancel subscription (delete or keep files)",
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
