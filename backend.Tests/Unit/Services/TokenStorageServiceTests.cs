using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using backend.Services;

namespace backend.Tests.Unit.Services;

public class TokenStorageServiceTests
{
    [Fact]
    public async Task GetTmdbTokenAsync_WhenUserTokenMissing_UsesAppsettingsFallback()
    {
        // Arrange
        var tempRoot = CreateTempDirectory();
        try
        {
            var sut = CreateService(
                tempRoot,
                new Dictionary<string, string?>
                {
                    ["PreFetch:TmdbToken"] = "tmdb_fallback_token"
                });

            // Act
            var token = await sut.GetTmdbTokenAsync();

            // Assert
            token.Should().Be("tmdb_fallback_token");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task GetTmdbTokenAsync_WhenBothConfigured_PrioritizesAppsettingsToken()
    {
        // Arrange
        var tempRoot = CreateTempDirectory();
        try
        {
            var sut = CreateService(
                tempRoot,
                new Dictionary<string, string?>
                {
                    ["PreFetch:TmdbToken"] = "tmdb_fallback_token"
                });

            await sut.SaveTokensAsync(null, "tmdb_stored_token");

            // Act
            var token = await sut.GetTmdbTokenAsync();

            // Assert
            token.Should().Be("tmdb_fallback_token");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task GetTmdbTokenAsync_WhenAppsettingsMissing_UsesStoredToken()
    {
        // Arrange
        var tempRoot = CreateTempDirectory();
        try
        {
            var sut = CreateService(tempRoot, new Dictionary<string, string?>());

            await sut.SaveTokensAsync(null, "tmdb_stored_token");

            // Act
            var token = await sut.GetTmdbTokenAsync();

            // Assert
            token.Should().Be("tmdb_stored_token");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static TokenStorageService CreateService(
        string contentRoot,
        Dictionary<string, string?> configValues)
    {
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.ContentRootPath).Returns(contentRoot);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        var keyDir = new DirectoryInfo(Path.Combine(contentRoot, ".keys"));
        keyDir.Create();

        var dataProtectionProvider = DataProtectionProvider.Create(keyDir);
        var logger = new Mock<ILogger<TokenStorageService>>();

        return new TokenStorageService(
            env.Object,
            configuration,
            dataProtectionProvider,
            logger.Object);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"anime-subscription-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
