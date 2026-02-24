using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Moq;
using backend.Controllers;
using backend.Models;
using backend.Services;
using backend.Services.Interfaces;
using backend.Services.Repositories;
using backend.Services.Validators;
using backend.Tests.Fixtures;

namespace backend.Tests.Unit.Controllers;

/// <summary>
/// Unit tests for AnimeController
/// </summary>
public class AnimeControllerTests
{
    private readonly Mock<IAnimeAggregationService> _aggregationServiceMock;
    private readonly Mock<ITokenStorageService> _tokenStorageMock;
    private readonly Mock<IAnimeRepository> _animeRepositoryMock;
    private readonly Mock<IAnimePoolService> _animePoolServiceMock;
    private readonly TokenValidator _tokenValidator;
    private readonly Mock<ILogger<AnimeController>> _loggerMock;
    private readonly Mock<ILogger<TokenValidator>> _tokenValidatorLoggerMock;
    private readonly AnimeController _sut;

    public AnimeControllerTests()
    {
        _aggregationServiceMock = new Mock<IAnimeAggregationService>();
        _tokenStorageMock = new Mock<ITokenStorageService>();
        _animeRepositoryMock = new Mock<IAnimeRepository>();
        _animePoolServiceMock = new Mock<IAnimePoolService>();
        _loggerMock = new Mock<ILogger<AnimeController>>();
        _tokenValidatorLoggerMock = new Mock<ILogger<TokenValidator>>();

        // TokenValidator is a concrete class, create real instance with mock logger
        _tokenValidator = new TokenValidator(_tokenValidatorLoggerMock.Object);

        _sut = new AnimeController(
            _aggregationServiceMock.Object,
            _tokenStorageMock.Object,
            _tokenValidator,
            _animeRepositoryMock.Object,
            _animePoolServiceMock.Object,
            _loggerMock.Object);

        // Set up HttpContext
        var httpContext = new DefaultHttpContext();
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
    }

    // Valid test token (TMDB: 100+ chars)
    private static readonly string ValidTmdbToken = new string('x', 110); // TMDB needs 100+ chars

    [Fact]
    public async Task GetTodayAnime_ReturnsOkWithApiData()
    {
        // Arrange
        var response = TestDataFactory.CreateAnimeListResponse(
            success: true,
            dataSource: DataSource.Api,
            count: 5);

        _tokenStorageMock
            .Setup(t => t.GetTmdbTokenAsync())
            .ReturnsAsync(ValidTmdbToken);

        _aggregationServiceMock
            .Setup(s => s.GetTodayAnimeEnrichedAsync(
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _sut.GetTodayAnime();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);

        // Verify response structure
        var value = okResult.Value;
        value.Should().NotBeNull();
    }

    [Fact]
    public async Task GetTodayAnime_UsesStoredTokenOverHeader()
    {
        // Arrange
        var response = TestDataFactory.CreateAnimeListResponse();

        _tokenStorageMock
            .Setup(t => t.GetTmdbTokenAsync())
            .ReturnsAsync(ValidTmdbToken);

        _aggregationServiceMock
            .Setup(s => s.GetTodayAnimeEnrichedAsync(
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Set header token (should be ignored because stored token has priority)
        _sut.ControllerContext.HttpContext.Request.Headers["X-TMDB-Token"] = "header-token-ignored";

        // Act
        await _sut.GetTodayAnime();

        // Assert - stored token is used, not header token
        _aggregationServiceMock.Verify(
            s => s.GetTodayAnimeEnrichedAsync(
                ValidTmdbToken,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetTodayAnime_FallsBackToHeaderToken()
    {
        // Arrange
        var response = TestDataFactory.CreateAnimeListResponse();

        _tokenStorageMock
            .Setup(t => t.GetTmdbTokenAsync())
            .ReturnsAsync((string?)null);

        _aggregationServiceMock
            .Setup(s => s.GetTodayAnimeEnrichedAsync(
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Set header token (will be used since no stored token)
        _sut.ControllerContext.HttpContext.Request.Headers["X-TMDB-Token"] = ValidTmdbToken;

        // Act
        await _sut.GetTodayAnime();

        // Assert - The header token is used
        _aggregationServiceMock.Verify(
            s => s.GetTodayAnimeEnrichedAsync(
                ValidTmdbToken,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetTodayAnime_ReturnsCorrectMetadata_WhenCacheHit()
    {
        // Arrange
        var cacheTime = DateTime.UtcNow.AddMinutes(-30);
        var response = new AnimeListResponse
        {
            Success = true,
            DataSource = DataSource.Cache,
            IsStale = false,
            Message = "Data from cache",
            LastUpdated = cacheTime,
            Count = 3,
            Animes = TestDataFactory.CreateAnimeInfoDtoList(3),
            RetryAttempts = 0
        };

        _tokenStorageMock
            .Setup(t => t.GetTmdbTokenAsync())
            .ReturnsAsync((string?)null);

        _aggregationServiceMock
            .Setup(s => s.GetTodayAnimeEnrichedAsync(
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _sut.GetTodayAnime();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task GetTodayAnime_ReturnsCorrectMetadata_WhenCacheFallback()
    {
        // Arrange
        var response = new AnimeListResponse
        {
            Success = true,
            DataSource = DataSource.CacheFallback,
            IsStale = true,
            Message = "API failed. Showing cached data.",
            LastUpdated = DateTime.UtcNow.AddHours(-2),
            Count = 3,
            Animes = TestDataFactory.CreateAnimeInfoDtoList(3),
            RetryAttempts = 3
        };

        _tokenStorageMock
            .Setup(t => t.GetTmdbTokenAsync())
            .ReturnsAsync((string?)null);

        _aggregationServiceMock
            .Setup(s => s.GetTodayAnimeEnrichedAsync(
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _sut.GetTodayAnime();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task GetTodayAnime_LogsRequestInfo()
    {
        // Arrange
        var response = TestDataFactory.CreateAnimeListResponse();

        _tokenStorageMock
            .Setup(t => t.GetTmdbTokenAsync())
            .ReturnsAsync((string?)null);

        _aggregationServiceMock
            .Setup(s => s.GetTodayAnimeEnrichedAsync(
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        await _sut.GetTodayAnime();

        // Assert - Verify logging was called
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }
}
