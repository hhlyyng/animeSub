using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using backend.Controllers;
using backend.Data;
using backend.Data.Entities;
using backend.Models.Dtos;
using backend.Services.Interfaces;

namespace backend.Tests.Unit.Controllers;

public class MikanControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;
    private readonly Mock<IMikanClient> _mikanClientMock;
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
        _qbittorrentServiceMock = new Mock<IQBittorrentService>();
        _loggerMock = new Mock<ILogger<MikanController>>();

        _sut = new MikanController(
            _mikanClientMock.Object,
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
            MagnetLink = "magnet:?xt=urn:btih:ABC123",
            TorrentHash = "ABC123",
            Title = "Test Torrent"
        };

        _qbittorrentServiceMock
            .Setup(s => s.AddTorrentWithTrackingAsync(
                request.MagnetLink,
                request.TorrentHash,
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

        var history = await db.DownloadHistory.SingleAsync(d => d.TorrentHash == request.TorrentHash);
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
                TorrentUrl = "magnet:?xt=urn:btih:EXISTING",
                TorrentHash = "EXISTING",
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
            MagnetLink = "magnet:?xt=urn:btih:EXISTING",
            TorrentHash = "EXISTING",
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

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _connection.Dispose();
    }
}
