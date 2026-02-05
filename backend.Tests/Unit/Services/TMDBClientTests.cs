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
/// Unit tests for TMDBClient - testing anime priority matching, year filtering, and season matching
/// </summary>
public class TMDBClientTests
{
    private readonly Mock<ILogger<TMDBClient>> _loggerMock;
    private readonly Mock<HttpMessageHandler> _httpHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly IOptions<ApiConfiguration> _config;

    public TMDBClientTests()
    {
        _loggerMock = new Mock<ILogger<TMDBClient>>();
        _httpHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpHandlerMock.Object)
        {
            BaseAddress = new Uri("https://api.themoviedb.org/3/")
        };
        _config = Options.Create(new ApiConfiguration
        {
            TMDB = new TMDBConfig
            {
                BaseUrl = "https://api.themoviedb.org/3",
                ImageBaseUrl = "https://image.tmdb.org/t/p/",
                TimeoutSeconds = 30
            }
        });
    }

    private TMDBClient CreateClient()
    {
        return new TMDBClient(_httpClient, _loggerMock.Object, _config);
    }

    private void SetupHttpResponses(params (string urlContains, string response)[] responses)
    {
        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                var url = request.RequestUri?.ToString() ?? "";
                foreach (var (urlContains, response) in responses)
                {
                    if (url.Contains(urlContains))
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(response, Encoding.UTF8, "application/json")
                        };
                    }
                }
                // Return empty response for unmatched URLs to avoid EnsureSuccessStatusCode throwing
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
            });
    }

    #region Anime Priority Matching Tests

    [Fact]
    public async Task GetAnimeSummaryAndBackdropAsync_WithMixedResults_SelectsAnime()
    {
        // Arrange: First result is live-action (no genre 16), second is anime (genre 16)
        var searchResponse = CreateSearchResponse(new[]
        {
            new SearchResult { Id = 1, Name = "Live Action Show", GenreIds = new[] { 18, 10759 }, OriginCountry = new[] { "US" } },
            new SearchResult { Id = 2, Name = "Anime Show", GenreIds = new[] { 16, 10759 }, OriginCountry = new[] { "JP" } }
        });

        var translationsResponse = CreateTranslationsResponse();

        SetupHttpResponses(
            ("search/tv", searchResponse),
            ("translations", translationsResponse)
        );

        var sut = CreateClient();
        sut.SetToken("test-token");

        // Act
        var result = await sut.GetAnimeSummaryAndBackdropAsync("Test Title");

        // Assert
        result.Should().NotBeNull();
        result!.TMDBID.Should().Be("2"); // Should select the anime (ID 2)
    }

    [Fact]
    public async Task GetAnimeSummaryAndBackdropAsync_WithMultipleAnime_SelectsJapanese()
    {
        // Arrange: Both are anime, but only second is Japanese
        var searchResponse = CreateSearchResponse(new[]
        {
            new SearchResult { Id = 1, Name = "US Animation", GenreIds = new[] { 16 }, OriginCountry = new[] { "US" } },
            new SearchResult { Id = 2, Name = "Japanese Anime", GenreIds = new[] { 16 }, OriginCountry = new[] { "JP" } }
        });

        var translationsResponse = CreateTranslationsResponse();

        SetupHttpResponses(
            ("search/tv", searchResponse),
            ("translations", translationsResponse)
        );

        var sut = CreateClient();
        sut.SetToken("test-token");

        // Act
        var result = await sut.GetAnimeSummaryAndBackdropAsync("Test Title");

        // Assert
        result.Should().NotBeNull();
        result!.TMDBID.Should().Be("2"); // Should prefer Japanese anime
    }

    [Fact]
    public async Task GetAnimeSummaryAndBackdropAsync_NoAnimeFound_FallsBackToFirst()
    {
        // Arrange: No anime results, only live-action
        var searchResponse = CreateSearchResponse(new[]
        {
            new SearchResult { Id = 1, Name = "Drama 1", GenreIds = new[] { 18 }, OriginCountry = new[] { "US" } },
            new SearchResult { Id = 2, Name = "Drama 2", GenreIds = new[] { 35 }, OriginCountry = new[] { "KR" } }
        });

        var translationsResponse = CreateTranslationsResponse();

        SetupHttpResponses(
            ("search/tv", searchResponse),
            ("translations", translationsResponse)
        );

        var sut = CreateClient();
        sut.SetToken("test-token");

        // Act
        var result = await sut.GetAnimeSummaryAndBackdropAsync("Test Title");

        // Assert
        result.Should().NotBeNull();
        result!.TMDBID.Should().Be("1"); // Should fallback to first result
    }

    #endregion

    #region Year Filtering Tests

    [Fact]
    public async Task GetAnimeSummaryAndBackdropAsync_WithAirDate_IncludesYearInSearch()
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
                if (req.RequestUri?.ToString().Contains("search/tv") == true)
                {
                    capturedUrl = req.RequestUri.ToString();
                }
            })
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                var url = request.RequestUri?.ToString() ?? "";
                if (url.Contains("search/tv"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(CreateSearchResponse(new[]
                        {
                            new SearchResult { Id = 1, Name = "Test", GenreIds = new[] { 16 }, OriginCountry = new[] { "JP" } }
                        }), Encoding.UTF8, "application/json")
                    };
                }
                if (url.Contains("translations"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(CreateTranslationsResponse(), Encoding.UTF8, "application/json")
                    };
                }
                if (url.Contains("tv/1") && !url.Contains("translations"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(CreateTvDetailsResponse(), Encoding.UTF8, "application/json")
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

        var sut = CreateClient();
        sut.SetToken("test-token");

        // Act
        await sut.GetAnimeSummaryAndBackdropAsync("Test Title", "2024-01-15");

        // Assert
        capturedUrl.Should().Contain("first_air_date_year=2024");
    }

    [Fact]
    public async Task GetAnimeSummaryAndBackdropAsync_WithoutAirDate_NoYearFilter()
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
                if (req.RequestUri?.ToString().Contains("search/tv") == true)
                {
                    capturedUrl = req.RequestUri.ToString();
                }
            })
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                var url = request.RequestUri?.ToString() ?? "";
                if (url.Contains("search/tv"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(CreateSearchResponse(new[]
                        {
                            new SearchResult { Id = 1, Name = "Test", GenreIds = new[] { 16 }, OriginCountry = new[] { "JP" } }
                        }), Encoding.UTF8, "application/json")
                    };
                }
                if (url.Contains("translations"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(CreateTranslationsResponse(), Encoding.UTF8, "application/json")
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

        var sut = CreateClient();
        sut.SetToken("test-token");

        // Act
        await sut.GetAnimeSummaryAndBackdropAsync("Test Title");

        // Assert
        capturedUrl.Should().NotContain("first_air_date_year");
    }

    #endregion

    #region No Results Tests

    [Fact]
    public async Task GetAnimeSummaryAndBackdropAsync_NoResults_ReturnsDefaultInfo()
    {
        // Arrange
        var emptyResponse = JsonSerializer.Serialize(new { results = Array.Empty<object>() });

        SetupHttpResponses(("search/tv", emptyResponse));

        var sut = CreateClient();
        sut.SetToken("test-token");

        // Act
        var result = await sut.GetAnimeSummaryAndBackdropAsync("Nonexistent Title");

        // Assert
        result.Should().NotBeNull();
        result!.EnglishSummary.Should().Be("No result found in TMDB.");
        result.BackdropUrl.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAnimeSummaryAndBackdropAsync_NoToken_ReturnsNull()
    {
        // Arrange
        var sut = CreateClient();
        // Don't set token

        // Act
        var result = await sut.GetAnimeSummaryAndBackdropAsync("Test Title");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Fallback Strategy Tests

    [Fact]
    public async Task GetAnimeSummaryAndBackdropAsync_OriginalTitleSucceeds_OnlyOneApiCall()
    {
        // Arrange: Original title returns results
        int apiCallCount = 0;
        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                var url = request.RequestUri?.ToString() ?? "";
                if (url.Contains("search/tv"))
                {
                    apiCallCount++;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(CreateSearchResponse(new[]
                        {
                            new SearchResult { Id = 1, Name = "Test", GenreIds = new[] { 16 }, OriginCountry = new[] { "JP" } }
                        }), Encoding.UTF8, "application/json")
                    };
                }
                if (url.Contains("translations"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(CreateTranslationsResponse(), Encoding.UTF8, "application/json")
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

        var sut = CreateClient();
        sut.SetToken("test-token");

        // Act
        var result = await sut.GetAnimeSummaryAndBackdropAsync("魔都精兵的奴隶 第二季", "2024-01-01");

        // Assert
        result.Should().NotBeNull();
        apiCallCount.Should().Be(1); // Only one search API call
    }

    [Fact]
    public async Task GetAnimeSummaryAndBackdropAsync_CleanedTitleSucceeds_TwoApiCalls()
    {
        // Arrange: Original title fails, cleaned title succeeds
        int apiCallCount = 0;
        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                var url = request.RequestUri?.ToString() ?? "";
                if (url.Contains("search/tv"))
                {
                    apiCallCount++;
                    // First call with season suffix fails, second with cleaned title succeeds
                    // Note: URL may be decoded when retrieved as string, so check for raw Chinese characters
                    if (url.Contains("第二季") || url.Contains("%E7%AC%AC%E4%BA%8C%E5%AD%A3"))
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(JsonSerializer.Serialize(new { results = Array.Empty<object>() }), Encoding.UTF8, "application/json")
                        };
                    }
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(CreateSearchResponse(new[]
                        {
                            new SearchResult { Id = 1, Name = "Test", GenreIds = new[] { 16 }, OriginCountry = new[] { "JP" } }
                        }), Encoding.UTF8, "application/json")
                    };
                }
                if (url.Contains("translations"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(CreateTranslationsResponse(), Encoding.UTF8, "application/json")
                    };
                }
                // Return empty JSON for other URLs to avoid EnsureSuccessStatusCode throwing
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
            });

        var sut = CreateClient();
        sut.SetToken("test-token");

        // Act
        var result = await sut.GetAnimeSummaryAndBackdropAsync("魔都精兵的奴隶 第二季", "2024-01-01");

        // Assert
        result.Should().NotBeNull();
        apiCallCount.Should().Be(2); // Two search API calls
    }

    [Fact]
    public async Task GetAnimeSummaryAndBackdropAsync_NoYearFilterSucceeds_ThreeApiCalls()
    {
        // Arrange: Both year-filtered queries fail, no-year query succeeds
        int apiCallCount = 0;
        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                var url = request.RequestUri?.ToString() ?? "";
                if (url.Contains("search/tv"))
                {
                    apiCallCount++;
                    // Queries with year filter fail
                    if (url.Contains("first_air_date_year"))
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(JsonSerializer.Serialize(new { results = Array.Empty<object>() }), Encoding.UTF8, "application/json")
                        };
                    }
                    // Query without year filter succeeds
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(CreateSearchResponse(new[]
                        {
                            new SearchResult { Id = 1, Name = "Test", GenreIds = new[] { 16 }, OriginCountry = new[] { "JP" } }
                        }), Encoding.UTF8, "application/json")
                    };
                }
                if (url.Contains("translations"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(CreateTranslationsResponse(), Encoding.UTF8, "application/json")
                    };
                }
                // Return empty JSON for other URLs to avoid EnsureSuccessStatusCode throwing
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
            });

        var sut = CreateClient();
        sut.SetToken("test-token");

        // Act
        var result = await sut.GetAnimeSummaryAndBackdropAsync("魔都精兵的奴隶 第二季", "2024-01-01");

        // Assert
        result.Should().NotBeNull();
        apiCallCount.Should().Be(3); // Three search API calls
    }

    [Fact]
    public async Task GetAnimeSummaryAndBackdropAsync_TitleWithoutSeason_SkipsCleanedTitleFallback()
    {
        // Arrange: Title has no season suffix, should skip layer 2
        int apiCallCount = 0;
        List<string> searchQueries = new();

        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                var url = request.RequestUri?.ToString() ?? "";
                if (url.Contains("search/tv"))
                {
                    apiCallCount++;
                    searchQueries.Add(url);
                    // First call with year filter fails
                    if (url.Contains("first_air_date_year"))
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(JsonSerializer.Serialize(new { results = Array.Empty<object>() }), Encoding.UTF8, "application/json")
                        };
                    }
                    // Call without year filter succeeds
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(CreateSearchResponse(new[]
                        {
                            new SearchResult { Id = 1, Name = "Test", GenreIds = new[] { 16 }, OriginCountry = new[] { "JP" } }
                        }), Encoding.UTF8, "application/json")
                    };
                }
                if (url.Contains("translations"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(CreateTranslationsResponse(), Encoding.UTF8, "application/json")
                    };
                }
                // Return empty JSON for other URLs to avoid EnsureSuccessStatusCode throwing
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
            });

        var sut = CreateClient();
        sut.SetToken("test-token");

        // Act
        var result = await sut.GetAnimeSummaryAndBackdropAsync("葬送のフリーレン", "2024-01-01");

        // Assert
        result.Should().NotBeNull();
        apiCallCount.Should().Be(2); // Original + no-year (skips cleaned title since no season suffix)
    }

    [Fact]
    public async Task GetAnimeSummaryAndBackdropAsync_NoAirDate_SkipsYearFallback()
    {
        // Arrange: No air date provided, should not have year fallback
        int tvSearchCount = 0;
        int movieSearchCount = 0;

        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                var url = request.RequestUri?.ToString() ?? "";
                if (url.Contains("search/tv"))
                {
                    tvSearchCount++;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(new { results = Array.Empty<object>() }), Encoding.UTF8, "application/json")
                    };
                }
                if (url.Contains("search/movie"))
                {
                    movieSearchCount++;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(new { results = Array.Empty<object>() }), Encoding.UTF8, "application/json")
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
            });

        var sut = CreateClient();
        sut.SetToken("test-token");

        // Act - No airDate provided
        var result = await sut.GetAnimeSummaryAndBackdropAsync("魔都精兵的奴隶 第二季");

        // Assert
        result.Should().NotBeNull();
        result!.EnglishSummary.Should().Be("No result found in TMDB.");
        // TV searches: original + cleaned (2 calls, no year fallback since no year was provided)
        // Movie searches: original + cleaned (2 calls as fallback when TV returns nothing)
        tvSearchCount.Should().Be(2);
        movieSearchCount.Should().Be(2);
    }

    #endregion

    #region Helper Methods

    private record SearchResult
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
        public int[] GenreIds { get; init; } = Array.Empty<int>();
        public string[] OriginCountry { get; init; } = Array.Empty<string>();
        public string? BackdropPath { get; init; }
        public string? Overview { get; init; }
    }

    private static string CreateSearchResponse(SearchResult[] results)
    {
        var response = new
        {
            results = results.Select(r => new
            {
                id = r.Id,
                name = r.Name,
                genre_ids = r.GenreIds,
                origin_country = r.OriginCountry,
                backdrop_path = r.BackdropPath ?? "/backdrop.jpg",
                overview = r.Overview ?? "Test overview"
            }).ToArray()
        };
        return JsonSerializer.Serialize(response);
    }

    private static string CreateTranslationsResponse()
    {
        return JsonSerializer.Serialize(new
        {
            translations = new[]
            {
                new
                {
                    iso_639_1 = "en",
                    iso_3166_1 = "US",
                    data = new
                    {
                        name = "English Title",
                        overview = "English overview"
                    }
                },
                new
                {
                    iso_639_1 = "zh",
                    iso_3166_1 = "CN",
                    data = new
                    {
                        name = "Chinese Title",
                        overview = "Chinese overview"
                    }
                }
            }
        });
    }

    private static string CreateTvDetailsResponse()
    {
        return JsonSerializer.Serialize(new
        {
            id = 1,
            name = "Test Anime",
            seasons = new[]
            {
                new { season_number = 1, air_date = "2020-01-10", poster_path = "/s1.jpg" },
                new { season_number = 2, air_date = "2022-04-01", poster_path = "/s2.jpg" },
                new { season_number = 3, air_date = "2024-01-06", poster_path = "/s3.jpg" }
            }
        });
    }

    #endregion
}
