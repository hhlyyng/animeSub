using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using backend.Controllers;
using backend.Data;
using backend.Data.Entities;
using backend.Models.Dtos;
using backend.Services.Exceptions;
using backend.Services.Interfaces;

namespace backend.Tests.Unit.Controllers;

public class MikanControllerTests : IDisposable
{
    private const string HashA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string HashB = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
    private const string Base32AllZeroHash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string HexAllZeroHash = "0000000000000000000000000000000000000000";

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;
    private readonly Mock<IMikanClient> _mikanClientMock;
    private readonly Mock<IBangumiClient> _bangumiClientMock;
    private readonly Mock<IAniListClient> _aniListClientMock;
    private readonly Mock<IJikanClient> _jikanClientMock;
    private readonly Mock<IQBittorrentService> _qbittorrentServiceMock;
    private readonly Mock<ILogger<MikanController>> _loggerMock;
    private readonly MikanController _sut;

    public MikanControllerTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<AnimeDbContext>(options => options.UseSqlite(_connection));
        _serviceProvider = services.BuildServiceProvider();

        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimeDbContext>();
            db.Database.EnsureCreated();
        }

        _mikanClientMock = new Mock<IMikanClient>();
        _bangumiClientMock = new Mock<IBangumiClient>();
        _aniListClientMock = new Mock<IAniListClient>();
        _jikanClientMock = new Mock<IJikanClient>();
        _qbittorrentServiceMock = new Mock<IQBittorrentService>();
        _loggerMock = new Mock<ILogger<MikanController>>();

        _bangumiClientMock
            .Setup(s => s.GetSubjectEpisodesAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(ParseJson("[]"));
        _bangumiClientMock
            .Setup(s => s.GetSubjectRelationsAsync(It.IsAny<int>()))
            .ReturnsAsync(ParseJson("[]"));
        _aniListClientMock
            .Setup(s => s.SearchAnimeSeasonDataAsync(It.IsAny<string>()))
            .ReturnsAsync((JsonElement?)null);
        _aniListClientMock
            .Setup(s => s.GetAnimeSeasonDataByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((JsonElement?)null);
        _jikanClientMock
            .Setup(s => s.GetAnimeDetailAsync(It.IsAny<int>()))
            .ReturnsAsync((JsonElement?)null);

        _sut = new MikanController(
            _mikanClientMock.Object,
            _bangumiClientMock.Object,
            _aniListClientMock.Object,
            _jikanClientMock.Object,
            _qbittorrentServiceMock.Object,
            _loggerMock.Object,
            _serviceProvider,
            new Mock<System.Net.Http.IHttpClientFactory>().Object);

        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    [Fact]
    public async Task Download_WhenManualSubscriptionMissing_CreatesSubscriptionAndHistory()
    {
        // Arrange
        var request = new DownloadTorrentRequest
        {
            MagnetLink = $"magnet:?xt=urn:btih:{HashA}",
            TorrentHash = HashA,
            Title = "Test Torrent"
        };

        _qbittorrentServiceMock
            .Setup(s => s.AddTorrentWithTrackingAsync(
                request.MagnetLink,
                HashA,
                request.Title,
                0,
                DownloadSource.Manual,
                null,
                null))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.Download(request);

        // Assert
        result.Should().BeOfType<OkObjectResult>();

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimeDbContext>();

        var manualSubscription = await db.Subscriptions.FirstOrDefaultAsync(s => s.Title == "__manual_download_tracking__");
        manualSubscription.Should().NotBeNull();
        manualSubscription!.IsEnabled.Should().BeFalse();

        var history = await db.DownloadHistory.SingleAsync(d => d.TorrentHash == HashA);
        history.SubscriptionId.Should().Be(manualSubscription.Id);
        history.Source.Should().Be(DownloadSource.Manual);
        history.Title.Should().Be(request.Title);
    }

    [Fact]
    public async Task Download_WhenHashAlreadyExists_DoesNotCallQbittorrentAgain()
    {
        // Arrange
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimeDbContext>();

            var subscription = new SubscriptionEntity
            {
                BangumiId = -1,
                Title = "__manual_download_tracking__",
                MikanBangumiId = "manual",
                IsEnabled = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            db.Subscriptions.Add(subscription);
            await db.SaveChangesAsync();

            db.DownloadHistory.Add(new DownloadHistoryEntity
            {
                SubscriptionId = subscription.Id,
                TorrentUrl = $"magnet:?xt=urn:btih:{HashB}",
                TorrentHash = HashB,
                Title = "Already Downloaded",
                Status = DownloadStatus.Pending,
                Source = DownloadSource.Manual,
                PublishedAt = DateTime.UtcNow,
                DiscoveredAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var request = new DownloadTorrentRequest
        {
            MagnetLink = $"magnet:?xt=urn:btih:{HashB}",
            TorrentHash = HashB,
            Title = "Already Downloaded"
        };

        // Act
        var result = await _sut.Download(request);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        _qbittorrentServiceMock.Verify(
            s => s.AddTorrentWithTrackingAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<DownloadSource>(),
                It.IsAny<int?>(),
                It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task Download_WhenRequestHashMissing_UsesHashFromMagnetAndSucceeds()
    {
        // Arrange
        var request = new DownloadTorrentRequest
        {
            MagnetLink = $"magnet:?xt=urn:btih:{Base32AllZeroHash}",
            TorrentHash = string.Empty,
            Title = "Base32 Hash Torrent"
        };

        _qbittorrentServiceMock
            .Setup(s => s.AddTorrentWithTrackingAsync(
                request.MagnetLink,
                HexAllZeroHash,
                request.Title,
                0,
                DownloadSource.Manual,
                null,
                null))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.Download(request);

        // Assert
        result.Should().BeOfType<OkObjectResult>();

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimeDbContext>();
        var history = await db.DownloadHistory.SingleAsync(d => d.TorrentHash == HexAllZeroHash);
        history.Title.Should().Be(request.Title);
    }

    [Fact]
    public async Task Download_WhenNoValidHashInRequest_ReturnsBadRequest()
    {
        // Arrange
        var request = new DownloadTorrentRequest
        {
            MagnetLink = "magnet:?dn=missing-hash",
            TorrentUrl = "https://example.com/download/nohash.torrent",
            TorrentHash = string.Empty,
            Title = "Invalid Hash Torrent"
        };

        // Act
        var result = await _sut.Download(request);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
        _qbittorrentServiceMock.Verify(
            s => s.AddTorrentWithTrackingAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<DownloadSource>(),
                It.IsAny<int?>(),
                It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task Download_WhenQbUnavailable_Returns503WithReason()
    {
        // Arrange
        var request = new DownloadTorrentRequest
        {
            MagnetLink = $"magnet:?xt=urn:btih:{HashA}",
            TorrentHash = HashA,
            Title = "Unavailable Torrent"
        };

        var retryAfter = DateTime.UtcNow.AddMinutes(1);
        _qbittorrentServiceMock
            .Setup(s => s.AddTorrentWithTrackingAsync(
                request.MagnetLink,
                HashA,
                request.Title,
                0,
                DownloadSource.Manual,
                null,
                null))
            .ThrowsAsync(new QBittorrentUnavailableException(
                "qBittorrent is offline or unreachable.",
                "timeout",
                retryAfter));

        // Act
        var result = await _sut.Download(request);

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public async Task Search_WhenBangumiIdProvided_CachesDefaultSeasonMikanId()
    {
        // Arrange
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimeDbContext>();
            db.AnimeInfos.Add(new AnimeInfoEntity
            {
                BangumiId = 1001,
                NameJapanese = "Test Anime",
                Weekday = 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        _mikanClientMock
            .Setup(s => s.SearchAnimeAsync("Test Anime"))
            .ReturnsAsync(new MikanSearchResult
            {
                AnimeTitle = "Test Anime",
                DefaultSeason = 1,
                Seasons = new List<MikanSeasonInfo>
                {
                    new() { SeasonName = "Season 1", MikanBangumiId = "201", Year = 2024 },
                    new() { SeasonName = "Season 2", MikanBangumiId = "202", Year = 2025 }
                }
            });

        // Act
        var response = await _sut.Search("Test Anime", "1001");

        // Assert
        response.Result.Should().BeOfType<OkObjectResult>();

        using var checkScope = _serviceProvider.CreateScope();
        var dbCheck = checkScope.ServiceProvider.GetRequiredService<AnimeDbContext>();
        var anime = await dbCheck.AnimeInfos.SingleAsync(a => a.BangumiId == 1001);
        anime.MikanBangumiId.Should().Be("202");
    }

    [Fact]
    public async Task Search_WhenSeasonConstraintProvided_CachesMatchedSeasonMikanId()
    {
        // Arrange
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimeDbContext>();
            db.AnimeInfos.Add(new AnimeInfoEntity
            {
                BangumiId = 2001,
                NameJapanese = "Test Anime Season 2",
                Weekday = 2,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        _mikanClientMock
            .Setup(s => s.SearchAnimeAsync("Test Anime Season 2"))
            .ReturnsAsync(new MikanSearchResult
            {
                AnimeTitle = "Test Anime",
                DefaultSeason = 0,
                Seasons = new List<MikanSeasonInfo>
                {
                    new() { SeasonName = "Season 1", MikanBangumiId = "301", Year = 2023, SeasonNumber = 1 },
                    new() { SeasonName = "Season 2", MikanBangumiId = "302", Year = 2025, SeasonNumber = 2 }
                }
            });

        // Act
        var response = await _sut.Search("Test Anime Season 2", "2001", 2);

        // Assert
        response.Result.Should().BeOfType<OkObjectResult>();
        var payload = ((OkObjectResult)response.Result!).Value.Should().BeAssignableTo<MikanSearchResult>().Subject;
        payload.Seasons.Should().HaveCount(1);
        payload.Seasons[0].MikanBangumiId.Should().Be("302");
        payload.DefaultSeason.Should().Be(0);

        using var checkScope = _serviceProvider.CreateScope();
        var dbCheck = checkScope.ServiceProvider.GetRequiredService<AnimeDbContext>();
        var anime = await dbCheck.AnimeInfos.SingleAsync(a => a.BangumiId == 2001);
        anime.MikanBangumiId.Should().Be("302");
    }

    [Fact]
    public async Task GetFeed_WhenEpisodeNumberingIsOffset_NormalizesEpisodesAndSetsLatestEpisode()
    {
        // Arrange
        _mikanClientMock
            .Setup(s => s.GetParsedFeedAsync("402"))
            .ReturnsAsync(new MikanFeedResponse
            {
                SeasonName = "Season 2",
                Items = new List<ParsedRssItem>
                {
                    new() { Title = "EP25", TorrentHash = "h1", Episode = 25, PublishedAt = DateTime.UtcNow.AddDays(-2) },
                    new() { Title = "EP26", TorrentHash = "h2", Episode = 26, PublishedAt = DateTime.UtcNow.AddDays(-1) }
                }
            });

        var bangumiDetail = JsonDocument.Parse("{\"eps\":12}").RootElement.Clone();
        _bangumiClientMock
            .Setup(s => s.GetSubjectDetailAsync(1001))
            .ReturnsAsync(bangumiDetail);

        // Act
        var response = await _sut.GetFeed("402", "1001");

        // Assert
        response.Result.Should().BeOfType<OkObjectResult>();
        var payload = ((OkObjectResult)response.Result!).Value.Should().BeAssignableTo<MikanFeedResponse>().Subject;
        payload.EpisodeOffset.Should().Be(24);
        payload.Items.Should().HaveCount(2);
        payload.Items[0].Episode.Should().Be(1);
        payload.Items[1].Episode.Should().Be(2);
        payload.LatestEpisode.Should().Be(2);
        payload.LatestPublishedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetFeed_WhenSeasonScopedAndAbsoluteEpisodesMixed_NormalizesAbsoluteEpisodesOnly()
    {
        // Arrange
        _mikanClientMock
            .Setup(s => s.GetParsedFeedAsync("999"))
            .ReturnsAsync(new MikanFeedResponse
            {
                SeasonName = "Season 2",
                Items = new List<ParsedRssItem>
                {
                    new() { Title = "[A] Frieren S02E03 [1080P]", TorrentHash = "h1", Episode = 3, PublishedAt = DateTime.UtcNow.AddDays(-3) },
                    new() { Title = "[A] Frieren S02E04 [1080P]", TorrentHash = "h2", Episode = 4, PublishedAt = DateTime.UtcNow.AddDays(-2) },
                    new() { Title = "[B] Frieren EP31 [1080P]", TorrentHash = "h3", Episode = 31, PublishedAt = DateTime.UtcNow.AddDays(-1) },
                    new() { Title = "[B] Frieren EP32 [1080P]", TorrentHash = "h4", Episode = 32, PublishedAt = DateTime.UtcNow }
                }
            });

        var bangumiDetail = JsonDocument.Parse("{\"eps\":12}").RootElement.Clone();
        _bangumiClientMock
            .Setup(s => s.GetSubjectDetailAsync(9999))
            .ReturnsAsync(bangumiDetail);

        // Act
        var response = await _sut.GetFeed("999", "9999");

        // Assert
        response.Result.Should().BeOfType<OkObjectResult>();
        var payload = ((OkObjectResult)response.Result!).Value.Should().BeAssignableTo<MikanFeedResponse>().Subject;
        payload.EpisodeOffset.Should().Be(28);

        var s02e04 = payload.Items.Single(i => i.Title.Contains("S02E04", StringComparison.OrdinalIgnoreCase));
        var ep32 = payload.Items.Single(i => i.Title.Contains("EP32", StringComparison.OrdinalIgnoreCase));

        s02e04.Episode.Should().Be(4);
        ep32.Episode.Should().Be(4);
        payload.LatestEpisode.Should().Be(4);
    }

    [Fact]
    public async Task GetFeed_WhenBangumiSortEpisodeMapExists_NormalizesAbsoluteEpisodesBySortMapping()
    {
        // Arrange
        _mikanClientMock
            .Setup(s => s.GetParsedFeedAsync("515759"))
            .ReturnsAsync(new MikanFeedResponse
            {
                SeasonName = "Season 2",
                Items = new List<ParsedRssItem>
                {
                    new() { Title = "[A] Frieren S02E04 [1080P]", TorrentHash = "h1", Episode = 4, PublishedAt = DateTime.UtcNow.AddDays(-1) },
                    new() { Title = "[B] Frieren EP32 [1080P]", TorrentHash = "h2", Episode = 32, PublishedAt = DateTime.UtcNow }
                }
            });

        _bangumiClientMock
            .Setup(s => s.GetSubjectDetailAsync(515759))
            .ReturnsAsync(ParseJson("{\"eps\":10,\"name\":\"Sousou no Frieren 2nd Season\"}"));
        _bangumiClientMock
            .Setup(s => s.GetSubjectEpisodesAsync(515759, It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(ParseJson("[{\"ep\":3,\"sort\":31},{\"ep\":4,\"sort\":32}]"));

        // Act
        var response = await _sut.GetFeed("515759", "515759");

        // Assert
        response.Result.Should().BeOfType<OkObjectResult>();
        var payload = ((OkObjectResult)response.Result!).Value.Should().BeAssignableTo<MikanFeedResponse>().Subject;
        payload.EpisodeOffset.Should().Be(28);
        payload.Items.Single(i => i.Title.Contains("S02E04", StringComparison.OrdinalIgnoreCase)).Episode.Should().Be(4);
        payload.Items.Single(i => i.Title.Contains("EP32", StringComparison.OrdinalIgnoreCase)).Episode.Should().Be(4);
    }

    [Fact]
    public async Task GetFeed_WhenSeasonHintTitleUsesAbsoluteEpisode_DoesNotTreatAbsoluteAsSeasonScoped()
    {
        // Arrange
        _mikanClientMock
            .Setup(s => s.GetParsedFeedAsync("3821"))
            .ReturnsAsync(new MikanFeedResponse
            {
                SeasonName = "Mikan Project - 葬送的芙莉莲 第二季",
                Items = new List<ParsedRssItem>
                {
                    new() { Title = "[黒ネズミたち] 葬送的芙莉莲 第二季 / Sousou no Frieren 2nd Season - 32", TorrentHash = "h1", Episode = 32, PublishedAt = DateTime.UtcNow },
                    new() { Title = "[Skymoon-Raws] 葬送的芙莉莲 第二季 / Sousou no Frieren 2nd Season - 04", TorrentHash = "h2", Episode = 4, PublishedAt = DateTime.UtcNow.AddMinutes(-1) }
                }
            });

        _bangumiClientMock
            .Setup(s => s.GetSubjectDetailAsync(515759))
            .ReturnsAsync(ParseJson("{\"eps\":10,\"name\":\"Sousou no Frieren 2nd Season\"}"));
        _bangumiClientMock
            .Setup(s => s.GetSubjectEpisodesAsync(515759, It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(ParseJson("[{\"ep\":3,\"sort\":31},{\"ep\":4,\"sort\":32}]"));

        // Act
        var response = await _sut.GetFeed("3821", "515759");

        // Assert
        response.Result.Should().BeOfType<OkObjectResult>();
        var payload = ((OkObjectResult)response.Result!).Value.Should().BeAssignableTo<MikanFeedResponse>().Subject;
        payload.EpisodeOffset.Should().Be(28);
        payload.Items.Single(i => i.Title.Contains("2nd Season - 32", StringComparison.OrdinalIgnoreCase)).Episode.Should().Be(4);
        payload.LatestEpisode.Should().Be(4);
    }

    [Fact]
    public async Task GetFeed_WhenBangumiPrequelRelationResolved_UsesPrequelOffsetFallback()
    {
        // Arrange
        _mikanClientMock
            .Setup(s => s.GetParsedFeedAsync("6000"))
            .ReturnsAsync(new MikanFeedResponse
            {
                SeasonName = "Season 2",
                Items = new List<ParsedRssItem>
                {
                    new() { Title = "Frieren EP31", TorrentHash = "h1", Episode = 31, PublishedAt = DateTime.UtcNow.AddDays(-1) },
                    new() { Title = "Frieren EP32", TorrentHash = "h2", Episode = 32, PublishedAt = DateTime.UtcNow }
                }
            });

        _bangumiClientMock
            .Setup(s => s.GetSubjectDetailAsync(6000))
            .ReturnsAsync(ParseJson("{\"eps\":10,\"name\":\"Sousou no Frieren 2nd Season\"}"));
        _bangumiClientMock
            .Setup(s => s.GetSubjectEpisodesAsync(6000, It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(ParseJson("[]"));
        _bangumiClientMock
            .Setup(s => s.GetSubjectRelationsAsync(6000))
            .ReturnsAsync(ParseJson("[{\"id\":400602,\"type\":2,\"relation\":\"前传\"}]"));
        _bangumiClientMock
            .Setup(s => s.GetSubjectDetailAsync(400602))
            .ReturnsAsync(ParseJson("{\"eps\":28}"));

        // Act
        var response = await _sut.GetFeed("6000", "6000");

        // Assert
        response.Result.Should().BeOfType<OkObjectResult>();
        var payload = ((OkObjectResult)response.Result!).Value.Should().BeAssignableTo<MikanFeedResponse>().Subject;
        payload.EpisodeOffset.Should().Be(28);
        payload.Items.Select(i => i.Episode).Should().BeEquivalentTo(new int?[] { 3, 4 });
    }

    [Fact]
    public async Task GetFeed_WhenBangumiOffsetUnavailable_UsesAniListAndJikanFallbackOffset()
    {
        // Arrange
        _mikanClientMock
            .Setup(s => s.GetParsedFeedAsync("7000"))
            .ReturnsAsync(new MikanFeedResponse
            {
                SeasonName = "Season 2",
                Items = new List<ParsedRssItem>
                {
                    new() { Title = "Frieren EP31", TorrentHash = "h1", Episode = 31, PublishedAt = DateTime.UtcNow.AddDays(-1) },
                    new() { Title = "Frieren EP32", TorrentHash = "h2", Episode = 32, PublishedAt = DateTime.UtcNow }
                }
            });

        _bangumiClientMock
            .Setup(s => s.GetSubjectDetailAsync(7000))
            .ReturnsAsync(ParseJson("{\"eps\":10,\"name\":\"Sousou no Frieren 2nd Season\"}"));
        _bangumiClientMock
            .Setup(s => s.GetSubjectEpisodesAsync(7000, It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(ParseJson("[]"));
        _bangumiClientMock
            .Setup(s => s.GetSubjectRelationsAsync(7000))
            .ReturnsAsync(ParseJson("[]"));

        _aniListClientMock
            .Setup(s => s.SearchAnimeSeasonDataAsync("Sousou no Frieren 2nd Season"))
            .ReturnsAsync(ParseJson("{\"id\":182255,\"relations\":{\"edges\":[{\"relationType\":\"PREQUEL\",\"node\":{\"id\":154587,\"idMal\":52991,\"type\":\"ANIME\",\"episodes\":null}}]}}"));
        _aniListClientMock
            .Setup(s => s.GetAnimeSeasonDataByIdAsync(154587))
            .ReturnsAsync(ParseJson("{\"id\":154587,\"relations\":{\"edges\":[]}}"));
        _jikanClientMock
            .Setup(s => s.GetAnimeDetailAsync(52991))
            .ReturnsAsync(ParseJson("{\"episodes\":28}"));

        // Act
        var response = await _sut.GetFeed("7000", "7000");

        // Assert
        response.Result.Should().BeOfType<OkObjectResult>();
        var payload = ((OkObjectResult)response.Result!).Value.Should().BeAssignableTo<MikanFeedResponse>().Subject;
        payload.EpisodeOffset.Should().Be(28);
        payload.Items.Select(i => i.Episode).Should().BeEquivalentTo(new int?[] { 3, 4 });
    }

    [Fact]
    public async Task GetFeed_WhenBangumiIsMovieOrSingleEpisode_ValidatesAsEpisodeOneAndSkipsOffsetStrategies()
    {
        // Arrange
        _mikanClientMock
            .Setup(s => s.GetParsedFeedAsync("23119"))
            .ReturnsAsync(new MikanFeedResponse
            {
                SeasonName = "Steins;Gate 剧场版",
                Items = new List<ParsedRssItem>
                {
                    new() { Title = "Steins;Gate Movie EP25", TorrentHash = "h1", Episode = 25, PublishedAt = DateTime.UtcNow.AddDays(-1) },
                    new() { Title = "Steins;Gate Movie EP26", TorrentHash = "h2", Episode = 26, PublishedAt = DateTime.UtcNow },
                    new() { Title = "Steins;Gate Movie Collection", TorrentHash = "h3", Episode = null, IsCollection = true, PublishedAt = DateTime.UtcNow.AddHours(-1) }
                }
            });

        _bangumiClientMock
            .Setup(s => s.GetSubjectDetailAsync(23119))
            .ReturnsAsync(ParseJson("{\"eps\":1,\"platform\":\"剧场版\",\"name_cn\":\"命运石之门 负荷领域的既视感\"}"));

        // Act
        var response = await _sut.GetFeed("23119", "23119");

        // Assert
        response.Result.Should().BeOfType<OkObjectResult>();
        var payload = ((OkObjectResult)response.Result!).Value.Should().BeAssignableTo<MikanFeedResponse>().Subject;
        payload.EpisodeOffset.Should().Be(0);
        payload.LatestEpisode.Should().Be(1);

        payload.Items.Where(i => !i.IsCollection).Select(i => i.Episode).Should().OnlyContain(ep => ep == 1);
        payload.Items.Single(i => i.IsCollection).Episode.Should().BeNull();

        _bangumiClientMock.Verify(s => s.GetSubjectEpisodesAsync(23119, It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        _bangumiClientMock.Verify(s => s.GetSubjectRelationsAsync(23119), Times.Never);
    }

    [Fact]
    public async Task PauseTorrent_WhenManualRecordExists_UpdatesStatusToPending()
    {
        // Arrange
        const string hash = "PAUSE001";
        await SeedManualDownloadAsync(hash, DownloadStatus.Downloading);

        // Act
        var response = await _sut.PauseTorrent(hash);

        // Assert
        response.Should().BeOfType<OkObjectResult>();
        _qbittorrentServiceMock.Verify(s => s.PauseTorrentAsync(hash), Times.Once);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimeDbContext>();
        var record = await db.DownloadHistory.SingleAsync(d => d.TorrentHash == hash);
        record.Status.Should().Be(DownloadStatus.Pending);
    }

    [Fact]
    public async Task ResumeTorrent_WhenManualRecordExists_UpdatesStatusToDownloading()
    {
        // Arrange
        const string hash = "RESUME001";
        await SeedManualDownloadAsync(hash, DownloadStatus.Pending);

        // Act
        var response = await _sut.ResumeTorrent(hash);

        // Assert
        response.Should().BeOfType<OkObjectResult>();
        _qbittorrentServiceMock.Verify(s => s.ResumeTorrentAsync(hash), Times.Once);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimeDbContext>();
        var record = await db.DownloadHistory.SingleAsync(d => d.TorrentHash == hash);
        record.Status.Should().Be(DownloadStatus.Downloading);
    }

    [Fact]
    public async Task RemoveTorrent_WhenDeleteSucceeds_RemovesManualRecords()
    {
        // Arrange
        const string hash = "REMOVE001";
        await SeedManualDownloadAsync(hash, DownloadStatus.Completed);

        _qbittorrentServiceMock
            .Setup(s => s.DeleteTorrentAsync(hash, false))
            .ReturnsAsync(true);

        // Act
        var response = await _sut.RemoveTorrent(hash, false);

        // Assert
        response.Should().BeOfType<OkObjectResult>();
        _qbittorrentServiceMock.Verify(s => s.DeleteTorrentAsync(hash, false), Times.Once);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimeDbContext>();
        var count = await db.DownloadHistory.CountAsync(d => d.TorrentHash == hash);
        count.Should().Be(0);
    }

    [Fact]
    public async Task GetTorrents_WhenQbProvidesRealtimeData_MergesRealtimeStateAndProgress()
    {
        // Arrange
        const string hash = "REALTIME001";
        await SeedManualDownloadAsync(hash, DownloadStatus.Pending);

        _qbittorrentServiceMock
            .Setup(s => s.GetTorrentsAsync(null))
            .ReturnsAsync(new List<QBTorrentInfo>
            {
                new()
                {
                    Hash = hash,
                    Name = "Realtime Name",
                    Size = 1000,
                    State = "downloading",
                    Progress = 76.5,
                    Dlspeed = 2048,
                    NumSeeds = 8,
                    NumLeechs = 2,
                    Eta = 120
                }
            });

        // Act
        var result = await _sut.GetTorrents();

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        var array = doc.RootElement;
        array.GetArrayLength().Should().Be(1);
        var root = array[0];

        root.GetProperty("hash").GetString().Should().Be(hash);
        root.GetProperty("name").GetString().Should().Be("Realtime Name");
        root.GetProperty("state").GetString().Should().Be("downloading");
        root.GetProperty("progress").GetDouble().Should().Be(76.5);
    }

    [Fact]
    public async Task GetTorrents_WhenSubscriptionSourceExists_ReturnsSourceAsSubscription()
    {
        // Arrange
        const string hash = "SUBSOURCE001";
        await SeedSubscriptionDownloadAsync(hash, DownloadStatus.Downloading);

        _qbittorrentServiceMock
            .Setup(s => s.GetTorrentsAsync(null))
            .ReturnsAsync(new List<QBTorrentInfo>());

        // Act
        var result = await _sut.GetTorrents();

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        var array = doc.RootElement;
        array.GetArrayLength().Should().Be(1);

        var root = array[0];
        root.GetProperty("hash").GetString().Should().Be(hash);
        root.GetProperty("source").GetString().Should().Be("subscription");
    }

    [Fact]
    public async Task GetTorrents_WhenQbUnavailable_Returns503()
    {
        // Arrange
        _qbittorrentServiceMock
            .Setup(s => s.GetTorrentsAsync(null))
            .ThrowsAsync(new QBittorrentUnavailableException(
                "qBittorrent is offline or unreachable.",
                "timeout",
                DateTime.UtcNow.AddMinutes(1)));

        // Act
        var result = await _sut.GetTorrents();

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    private static JsonElement ParseJson(string json)
    {
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private async Task SeedManualDownloadAsync(string hash, DownloadStatus status)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimeDbContext>();

        var subscription = await db.Subscriptions.FirstOrDefaultAsync(s => s.Title == "__manual_download_tracking__");
        if (subscription == null)
        {
            subscription = new SubscriptionEntity
            {
                BangumiId = -1,
                Title = "__manual_download_tracking__",
                MikanBangumiId = "manual",
                IsEnabled = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Subscriptions.Add(subscription);
            await db.SaveChangesAsync();
        }

        db.DownloadHistory.Add(new DownloadHistoryEntity
        {
            SubscriptionId = subscription.Id,
            TorrentUrl = $"magnet:?xt=urn:btih:{hash}",
            TorrentHash = hash,
            Title = $"Title-{hash}",
            Status = status,
            Source = DownloadSource.Manual,
            PublishedAt = DateTime.UtcNow,
            DiscoveredAt = DateTime.UtcNow,
            DownloadedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }

    private async Task SeedSubscriptionDownloadAsync(string hash, DownloadStatus status)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimeDbContext>();

        var subscription = await db.Subscriptions.FirstOrDefaultAsync(s => s.BangumiId == 10086);
        if (subscription == null)
        {
            subscription = new SubscriptionEntity
            {
                BangumiId = 10086,
                Title = "Subscription Download",
                MikanBangumiId = "10086",
                IsEnabled = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Subscriptions.Add(subscription);
            await db.SaveChangesAsync();
        }

        db.DownloadHistory.Add(new DownloadHistoryEntity
        {
            SubscriptionId = subscription.Id,
            TorrentUrl = $"magnet:?xt=urn:btih:{hash}",
            TorrentHash = hash,
            Title = $"Title-{hash}",
            Status = status,
            Source = DownloadSource.Subscription,
            PublishedAt = DateTime.UtcNow,
            DiscoveredAt = DateTime.UtcNow,
            DownloadedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _connection.Dispose();
    }
}
