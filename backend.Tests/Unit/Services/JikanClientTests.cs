using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using backend.Models.Configuration;
using backend.Services.Implementations;

namespace backend.Tests.Unit.Services;

/// <summary>
/// Unit tests for JikanClient - testing MAL Top anime fetching
/// </summary>
public class JikanClientTests
{
    private readonly Mock<ILogger<JikanClient>> _loggerMock;
    private readonly Mock<HttpMessageHandler> _httpHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly IOptions<ApiConfiguration> _config;

    public JikanClientTests()
    {
        _loggerMock = new Mock<ILogger<JikanClient>>();
        _httpHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpHandlerMock.Object)
        {
            BaseAddress = new Uri("https://api.jikan.moe/v4/")
        };
        _config = Options.Create(new ApiConfiguration
        {
            Jikan = new JikanConfig
            {
                BaseUrl = "https://api.jikan.moe/v4",
                TimeoutSeconds = 30
            }
        });
    }

    private JikanClient CreateClient()
    {
        return new JikanClient(_httpClient, _loggerMock.Object, _config);
    }

    private void SetupHttpResponse(string response, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            });
    }

    [Fact]
    public async Task GetTopAnimeAsync_ReturnsTop10Anime()
    {
        // Arrange
        var response = CreateJikanTopAnimeResponse(10);
        SetupHttpResponse(response);

        var sut = CreateClient();

        // Act
        var result = await sut.GetTopAnimeAsync(10);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(10);
    }

    [Fact]
    public async Task GetTopAnimeAsync_MapsFieldsCorrectly()
    {
        // Arrange
        var response = CreateJikanTopAnimeResponse(1);
        SetupHttpResponse(response);

        var sut = CreateClient();

        // Act
        var result = await sut.GetTopAnimeAsync(1);

        // Assert
        result.Should().HaveCount(1);
        var anime = result[0];
        anime.MalId.Should().Be(1);
        anime.Title.Should().Be("Test Anime 1");
        anime.TitleJapanese.Should().Be("テストアニメ 1");
        anime.TitleEnglish.Should().Be("Test Anime EN 1");
        anime.Score.Should().BeApproximately(9.01, 0.1);
        anime.Synopsis.Should().NotBeEmpty();
        anime.Url.Should().Contain("myanimelist.net");
        anime.Images.Should().NotBeNull();
        anime.Images!.Jpg.Should().NotBeNull();
        anime.Images.Jpg!.LargeImageUrl.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetTopAnimeAsync_EmptyResponse_ReturnsEmptyList()
    {
        // Arrange
        var response = JsonSerializer.Serialize(new { data = Array.Empty<object>() });
        SetupHttpResponse(response);

        var sut = CreateClient();

        // Act
        var result = await sut.GetTopAnimeAsync(10);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTopAnimeAsync_ApiError_ThrowsException()
    {
        // Arrange
        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        var sut = CreateClient();

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(() => sut.GetTopAnimeAsync(10));
    }

    [Fact]
    public async Task GetTopAnimeAsync_RequestsCorrectEndpoint()
    {
        // Arrange
        string capturedUrl = "";
        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capturedUrl = req.RequestUri?.ToString() ?? "";
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(CreateJikanTopAnimeResponse(5), Encoding.UTF8, "application/json")
            });

        var sut = CreateClient();

        // Act
        await sut.GetTopAnimeAsync(5);

        // Assert
        capturedUrl.Should().Contain("top/anime");
        capturedUrl.Should().Contain("limit=5");
    }

    #region Helper Methods

    private static string CreateJikanTopAnimeResponse(int count)
    {
        var animes = Enumerable.Range(1, count).Select(i => new
        {
            mal_id = i,
            url = $"https://myanimelist.net/anime/{i}",
            title = $"Test Anime {i}",
            title_japanese = $"テストアニメ {i}",
            title_english = $"Test Anime EN {i}",
            synopsis = $"This is a test synopsis for anime {i}",
            score = 9.0 + (i * 0.01),
            scored_by = 10000 + i,
            rank = i,
            popularity = i * 10,
            status = "Currently Airing",
            images = new
            {
                jpg = new
                {
                    image_url = $"https://cdn.myanimelist.net/images/anime/{i}.jpg",
                    small_image_url = $"https://cdn.myanimelist.net/images/anime/{i}_small.jpg",
                    large_image_url = $"https://cdn.myanimelist.net/images/anime/{i}_large.jpg"
                },
                webp = new
                {
                    image_url = $"https://cdn.myanimelist.net/images/anime/{i}.webp",
                    small_image_url = $"https://cdn.myanimelist.net/images/anime/{i}_small.webp",
                    large_image_url = $"https://cdn.myanimelist.net/images/anime/{i}_large.webp"
                }
            }
        }).ToArray();

        return JsonSerializer.Serialize(new
        {
            data = animes,
            pagination = new
            {
                last_visible_page = 1,
                has_next_page = false
            }
        });
    }

    #endregion
}
