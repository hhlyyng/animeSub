global using System.Text.Json;
global using System.Net.Http;
using Serilog;
using backend.Services.Interfaces;
using backend.Services.Implementations;
using backend.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName)
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
app.UseGlobalExceptionHandler(); // Must be first to catch all exceptions
app.UseResponseCompression();
app.UseCors("AllowFrontend");

// Map controllers
app.MapControllers();

// Health check endpoint
app.MapGet("/", () => new
{
    status = "running",
    timestamp = DateTime.UtcNow,
    endpoints = new[]
    {
        "GET /api/anime/today - Get today's anime schedule"
    }
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