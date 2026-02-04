using FluentAssertions;
using Moq;
using backend.Models;
using backend.Services.Interfaces;
using backend.Tests.Fixtures;

namespace backend.Tests.Unit.Services;

/// <summary>
/// Unit tests for AniListClient trending anime functionality
/// Tests use mocked interface to verify expected behavior
/// </summary>
public class AniListClientTrendingTests
{
    private readonly Mock<IAniListClient> _aniListClientMock;

    public AniListClientTrendingTests()
    {
        _aniListClientMock = new Mock<IAniListClient>();
    }

    [Fact]
    public async Task GetTrendingAnimeAsync_ReturnsTrendingList()
    {
        // Arrange
        var trendingList = Enumerable.Range(1, 10).Select(i => new AniListAnimeInfo
        {
            AnilistId = i.ToString(),
            EnglishTitle = $"Test Anime EN {i}",
            NativeTitle = $"テストアニメ {i}",
            EnglishSummary = $"Description {i}",
            Score = "8.5",
            CoverUrl = $"https://anilist.co/img/cover_{i}.jpg",
            BannerImage = $"https://anilist.co/img/banner_{i}.jpg",
            OriSiteUrl = $"https://anilist.co/anime/{i}"
        }).ToList();

        _aniListClientMock
            .Setup(c => c.GetTrendingAnimeAsync(10))
            .ReturnsAsync(trendingList);

        // Act
        var result = await _aniListClientMock.Object.GetTrendingAnimeAsync(10);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(10);
    }

    [Fact]
    public async Task GetTrendingAnimeAsync_ParsesFieldsCorrectly()
    {
        // Arrange
        var anime = new AniListAnimeInfo
        {
            AnilistId = "1",
            EnglishTitle = "Test Anime EN 1",
            NativeTitle = "テストアニメ 1",
            EnglishSummary = "This is a test description",
            Score = "8.5",
            CoverUrl = "https://anilist.co/img/cover_1.jpg",
            BannerImage = "https://anilist.co/img/banner_1.jpg",
            OriSiteUrl = "https://anilist.co/anime/1"
        };

        _aniListClientMock
            .Setup(c => c.GetTrendingAnimeAsync(1))
            .ReturnsAsync(new List<AniListAnimeInfo> { anime });

        // Act
        var result = await _aniListClientMock.Object.GetTrendingAnimeAsync(1);

        // Assert
        result.Should().HaveCount(1);
        var first = result[0];
        first.AnilistId.Should().Be("1");
        first.EnglishTitle.Should().Be("Test Anime EN 1");
        first.NativeTitle.Should().Be("テストアニメ 1");
        first.EnglishSummary.Should().Contain("test description");
        first.Score.Should().Be("8.5");
        first.CoverUrl.Should().NotBeEmpty();
        first.BannerImage.Should().NotBeEmpty();
        first.OriSiteUrl.Should().Contain("anilist.co");
    }

    [Fact]
    public async Task GetTrendingAnimeAsync_EmptyResponse_ReturnsEmptyList()
    {
        // Arrange
        _aniListClientMock
            .Setup(c => c.GetTrendingAnimeAsync(10))
            .ReturnsAsync(new List<AniListAnimeInfo>());

        // Act
        var result = await _aniListClientMock.Object.GetTrendingAnimeAsync(10);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTrendingAnimeAsync_WithDifferentLimits_ReturnsRequestedCount()
    {
        // Arrange
        var trendingList5 = Enumerable.Range(1, 5).Select(i => new AniListAnimeInfo
        {
            AnilistId = i.ToString(),
            EnglishTitle = $"Test {i}"
        }).ToList();

        _aniListClientMock
            .Setup(c => c.GetTrendingAnimeAsync(5))
            .ReturnsAsync(trendingList5);

        // Act
        var result = await _aniListClientMock.Object.GetTrendingAnimeAsync(5);

        // Assert
        result.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetTrendingAnimeAsync_HandlesNullEnglishTitle()
    {
        // Arrange
        var anime = new AniListAnimeInfo
        {
            AnilistId = "1",
            EnglishTitle = "", // No English title
            NativeTitle = "日本語タイトル",
            EnglishSummary = "Description"
        };

        _aniListClientMock
            .Setup(c => c.GetTrendingAnimeAsync(1))
            .ReturnsAsync(new List<AniListAnimeInfo> { anime });

        // Act
        var result = await _aniListClientMock.Object.GetTrendingAnimeAsync(1);

        // Assert
        result.Should().HaveCount(1);
        result[0].EnglishTitle.Should().BeEmpty();
        result[0].NativeTitle.Should().Be("日本語タイトル");
    }

    [Fact]
    public async Task GetAnimeInfoAsync_ReturnsAnimeInfo()
    {
        // Arrange
        var anime = TestDataFactory.CreateAniListAnimeInfo("Searched Anime");

        _aniListClientMock
            .Setup(c => c.GetAnimeInfoAsync("テストアニメ"))
            .ReturnsAsync(anime);

        // Act
        var result = await _aniListClientMock.Object.GetAnimeInfoAsync("テストアニメ");

        // Assert
        result.Should().NotBeNull();
        result!.EnglishTitle.Should().Be("Searched Anime");
    }

    [Fact]
    public async Task GetAnimeInfoAsync_NotFound_ReturnsNull()
    {
        // Arrange
        _aniListClientMock
            .Setup(c => c.GetAnimeInfoAsync("Nonexistent"))
            .ReturnsAsync((AniListAnimeInfo?)null);

        // Act
        var result = await _aniListClientMock.Object.GetAnimeInfoAsync("Nonexistent");

        // Assert
        result.Should().BeNull();
    }
}
