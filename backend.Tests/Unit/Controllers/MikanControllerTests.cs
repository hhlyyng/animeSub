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
        _qbittorrentServiceMock = new Mock<IQBittorrentService>();
        _loggerMock = new Mock<ILogger<MikanController>>();

        _sut = new MikanController(
            _mikanClientMock.Object,
            _bangumiClientMock.Object,
            _qbittorrentServiceMock.Object,
            _loggerMock.Object,
            _serviceProvider);

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
                It.IsAny<int?>()),
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
                It.IsAny<int?>()),
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

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _connection.Dispose();
    }
}
