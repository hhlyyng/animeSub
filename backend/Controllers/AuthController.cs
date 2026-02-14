using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using backend.Services;
using backend.Services.Interfaces;

namespace backend.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private static readonly SemaphoreSlim RuntimeSettingsFileLock = new(1, 1);
    private const string RuntimeSettingsFileName = "appsettings.runtime.json";
    private static readonly string[] AllowedImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private const long MaxImageSize = 5 * 1024 * 1024; // 5MB

    private readonly IAuthService _authService;
    private readonly ITokenStorageService _tokenStorage;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAuthService authService,
        ITokenStorageService tokenStorage,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<AuthController> logger)
    {
        _authService = authService;
        _tokenStorage = tokenStorage;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    [HttpGet("status")]
    [AllowAnonymous]
    public async Task<IActionResult> GetStatus()
    {
        var isSetup = await _authService.IsSetupCompletedAsync();
        var isAuthenticated = false;

        var authHeader = Request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authHeader["Bearer ".Length..].Trim();
            isAuthenticated = _authService.ValidateToken(token);
        }

        return Ok(new { isSetupCompleted = isSetup, isAuthenticated });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "Username and password are required" });
        }

        var token = await _authService.LoginAsync(request.Username, request.Password);
        if (token == null)
        {
            return Unauthorized(new { message = "Invalid username or password" });
        }

        var expirationDays = _configuration.GetValue("Auth:TokenExpirationDays", 7);
        return Ok(new
        {
            token,
            username = request.Username.Trim(),
            expiresAt = DateTime.UtcNow.AddDays(expirationDays).ToString("o")
        });
    }

    [HttpPost("setup")]
    [AllowAnonymous]
    public async Task<IActionResult> Setup([FromBody] SetupRequest request)
    {
        if (await _authService.IsSetupCompletedAsync())
        {
            return Conflict(new { message = "Setup already completed" });
        }

        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "Username and password are required" });
        }

        if (string.IsNullOrWhiteSpace(request.TmdbToken))
        {
            return BadRequest(new { message = "TMDB token is required" });
        }

        if (request.Qbittorrent == null)
        {
            return BadRequest(new { message = "qBittorrent settings are required" });
        }

        try
        {
            // Save settings to runtime config
            await SaveRuntimeSettingsAsync(root =>
            {
                var apiTokens = EnsureSection(root, "ApiTokens");
                apiTokens["TmdbToken"] = request.TmdbToken.Trim();

                var qb = EnsureSection(root, "QBittorrent");
                qb["Host"] = request.Qbittorrent.Host?.Trim() ?? "localhost";
                qb["Port"] = request.Qbittorrent.Port ?? 8080;
                qb["Username"] = request.Qbittorrent.Username?.Trim() ?? "admin";
                qb["Password"] = request.Qbittorrent.Password ?? "";
                qb["DefaultSavePath"] = request.Qbittorrent.DefaultSavePath?.Trim() ?? "";
                qb["Category"] = string.IsNullOrWhiteSpace(request.Qbittorrent.Category)
                    ? "anime" : request.Qbittorrent.Category.Trim();
                qb["Tags"] = string.IsNullOrWhiteSpace(request.Qbittorrent.Tags)
                    ? "AnimeSub" : request.Qbittorrent.Tags.Trim();

                if (request.DownloadPreferences != null)
                {
                    var dp = EnsureSection(root, "DownloadPreferences");
                    dp["Subgroup"] = request.DownloadPreferences.Subgroup?.Trim() ?? "all";
                    dp["Resolution"] = request.DownloadPreferences.Resolution?.Trim() ?? "1080P";
                    dp["SubtitleType"] = request.DownloadPreferences.SubtitleType?.Trim() ?? "\u7b80\u65e5\u5185\u5d4c";

                    if (request.DownloadPreferences.PriorityOrder is { Count: 3 })
                    {
                        var arr = new JsonArray();
                        foreach (var field in request.DownloadPreferences.PriorityOrder)
                        {
                            arr.Add(field);
                        }
                        dp["PriorityOrder"] = arr;
                    }
                }
            });

            // Save TMDB token to encrypted storage
            await _tokenStorage.SaveTmdbTokenAsync(request.TmdbToken.Trim());

            // Create user account
            await _authService.SetupUserAsync(request.Username, request.Password);

            _logger.LogInformation("Setup completed for user: {Username}", request.Username.Trim());
            return Ok(new { message = "Setup completed successfully" });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Setup failed");
            return StatusCode(500, new { message = "Setup failed" });
        }
    }

    [HttpGet("background")]
    [AllowAnonymous]
    public IActionResult GetBackground()
    {
        var uploadsDir = Path.Combine(_environment.ContentRootPath, "Data", "uploads");
        if (!Directory.Exists(uploadsDir))
        {
            return NotFound();
        }

        foreach (var ext in AllowedImageExtensions)
        {
            var filePath = Path.Combine(uploadsDir, $"login-background{ext}");
            if (System.IO.File.Exists(filePath))
            {
                var contentType = ext switch
                {
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".png" => "image/png",
                    ".webp" => "image/webp",
                    _ => "application/octet-stream"
                };
                return PhysicalFile(filePath, contentType);
            }
        }

        return NotFound();
    }

    [HttpPost("background")]
    [Authorize]
    public async Task<IActionResult> UploadBackground(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "No file uploaded" });
        }

        if (file.Length > MaxImageSize)
        {
            return BadRequest(new { message = "File size exceeds 5MB limit" });
        }

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedImageExtensions.Contains(ext))
        {
            return BadRequest(new { message = "Only jpg, png, and webp files are allowed" });
        }

        var uploadsDir = Path.Combine(_environment.ContentRootPath, "Data", "uploads");
        Directory.CreateDirectory(uploadsDir);

        // Remove existing background files
        foreach (var allowedExt in AllowedImageExtensions)
        {
            var existing = Path.Combine(uploadsDir, $"login-background{allowedExt}");
            if (System.IO.File.Exists(existing))
            {
                System.IO.File.Delete(existing);
            }
        }

        var filePath = Path.Combine(uploadsDir, $"login-background{ext}");
        await using var stream = new FileStream(filePath, FileMode.Create);
        await file.CopyToAsync(stream);

        _logger.LogInformation("Login background uploaded: {FileName}", file.FileName);
        return Ok(new { message = "Background uploaded" });
    }

    [HttpDelete("background")]
    [Authorize]
    public IActionResult DeleteBackground()
    {
        var uploadsDir = Path.Combine(_environment.ContentRootPath, "Data", "uploads");
        var deleted = false;

        foreach (var ext in AllowedImageExtensions)
        {
            var filePath = Path.Combine(uploadsDir, $"login-background{ext}");
            if (System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);
                deleted = true;
            }
        }

        return Ok(new { message = deleted ? "Background removed" : "No background to remove" });
    }

    private async Task SaveRuntimeSettingsAsync(Action<JsonObject> mutate)
    {
        var filePath = Path.Combine(_environment.ContentRootPath, RuntimeSettingsFileName);

        await RuntimeSettingsFileLock.WaitAsync();
        try
        {
            JsonObject root;
            if (System.IO.File.Exists(filePath))
            {
                try
                {
                    var existing = await System.IO.File.ReadAllTextAsync(filePath);
                    root = JsonNode.Parse(existing) as JsonObject ?? new JsonObject();
                }
                catch
                {
                    root = new JsonObject();
                }
            }
            else
            {
                root = new JsonObject();
            }

            mutate(root);

            var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            await System.IO.File.WriteAllTextAsync(filePath, json);
        }
        finally
        {
            RuntimeSettingsFileLock.Release();
        }
    }

    private static JsonObject EnsureSection(JsonObject root, string sectionName)
    {
        if (root[sectionName] is JsonObject existing)
        {
            return existing;
        }

        var section = new JsonObject();
        root[sectionName] = section;
        return section;
    }
}

public record LoginRequest(string? Username, string? Password);

public record SetupRequest(
    string? Username,
    string? Password,
    string? TmdbToken,
    SetupQbittorrentRequest? Qbittorrent,
    SetupDownloadPreferencesRequest? DownloadPreferences);

public record SetupQbittorrentRequest(
    string? Host,
    int? Port,
    string? Username,
    string? Password,
    string? DefaultSavePath,
    string? Category,
    string? Tags);

public record SetupDownloadPreferencesRequest(
    string? Subgroup,
    string? Resolution,
    string? SubtitleType,
    List<string>? PriorityOrder);
