using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using backend.Data;
using backend.Data.Entities;
using backend.Services.Interfaces;

namespace backend.Services.Implementations;

public class AuthService : IAuthService
{
    private readonly AnimeDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<AuthService> _logger;
    private static readonly SemaphoreSlim SetupLock = new(1, 1);

    public AuthService(
        AnimeDbContext db,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<AuthService> logger)
    {
        _db = db;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    public async Task<bool> IsSetupCompletedAsync()
    {
        return await _db.Users.AnyAsync();
    }

    public async Task SetupUserAsync(string username, string password)
    {
        await SetupLock.WaitAsync();
        try
        {
            if (await _db.Users.AnyAsync())
            {
                throw new InvalidOperationException("Setup already completed");
            }

            var passwordHash = BCrypt.Net.BCrypt.HashPassword(password);
            var user = new UserEntity
            {
                Username = username.Trim(),
                PasswordHash = passwordHash,
                CreatedAt = DateTime.UtcNow
            };

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            await EnsureJwtSecretAsync();
            _logger.LogInformation("User setup completed for: {Username}", username);
        }
        finally
        {
            SetupLock.Release();
        }
    }

    public async Task<string?> LoginAsync(string username, string password)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username.Trim());
        if (user == null)
        {
            return null;
        }

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            return null;
        }

        var secret = await EnsureJwtSecretAsync();
        var expirationDays = _configuration.GetValue("Auth:TokenExpirationDays", 7);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Username),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        var token = new JwtSecurityToken(
            issuer: "AnimeSub",
            audience: "AnimeSub",
            claims: claims,
            expires: DateTime.UtcNow.AddDays(expirationDays),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public bool ValidateToken(string token)
    {
        var secret = _configuration["Auth:JwtSecret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        try
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
            var handler = new JwtSecurityTokenHandler();
            handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = "AnimeSub",
                ValidateAudience = true,
                ValidAudience = "AnimeSub",
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(5)
            }, out _);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> EnsureJwtSecretAsync()
    {
        var existing = _configuration["Auth:JwtSecret"];
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var filePath = Path.Combine(_environment.ContentRootPath, "appsettings.runtime.json");

        JsonObject root;
        if (File.Exists(filePath))
        {
            try
            {
                var content = await File.ReadAllTextAsync(filePath);
                root = JsonNode.Parse(content) as JsonObject ?? new JsonObject();
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

        var auth = root["Auth"] as JsonObject;
        if (auth == null)
        {
            auth = new JsonObject();
            root["Auth"] = auth;
        }

        auth["JwtSecret"] = secret;

        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(filePath, json);

        _logger.LogInformation("Generated and persisted new JWT secret");
        return secret;
    }
}
