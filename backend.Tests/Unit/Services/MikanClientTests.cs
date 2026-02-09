using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using backend.Data;
using backend.Data.Entities;
using backend.Models.Configuration;
using backend.Services.Implementations;

namespace backend.Tests.Unit.Services;

public class MikanClientTests
{
    [Fact]
    public async Task GetAnimeFeedAsync_WhenPubDateExistsUnderTorrentNamespace_ParsesPublishedAtFromThatField()
    {
        // Arrange
        const string xml = """
                           <rss version="2.0">
                             <channel>
                               <title>Test Feed</title>
                               <item>
                                 <guid isPermaLink="false">test-guid</guid>
                                 <title>[Group] Test Anime - 01 [1080P]</title>
                                 <link>https://mikan.tangbai.cc/Home/Episode/abc</link>
                                 <description>desc</description>
                                 <torrent xmlns="https://mikan.tangbai.cc/0.1/">
                                   <pubDate>2025-06-15T12:34:56+00:00</pubDate>
                                 </torrent>
                                 <enclosure type="application/x-bittorrent" length="123456" url="https://mikan.tangbai.cc/Download/test.torrent" />
                               </item>
                             </channel>
                           </rss>
                           """;

        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(xml, Encoding.UTF8, "application/xml")
            });

        var httpClient = new HttpClient(handlerMock.Object);
        var config = Options.Create(new MikanConfiguration
        {
            BaseUrl = "https://mikan.tangbai.cc",
            TimeoutSeconds = 30
        });

        var logger = new Mock<ILogger<MikanClient>>();
        var parser = new TorrentTitleParser();
        var sut = new MikanClient(httpClient, logger.Object, config, parser);

        // Act
        var feed = await sut.GetAnimeFeedAsync("3849");

        // Assert
        feed.Items.Should().HaveCount(1);
        feed.Items[0].PublishedAt.Should().Be(new DateTime(2025, 6, 15, 12, 34, 56, DateTimeKind.Utc));
    }

    [Fact]
    public async Task SearchAnimeAsync_WhenFormEncodedQueryHasNoResult_RetriesWithPlusJoinedQuery()
    {
        // Arrange
        const string title = "\u547d\u8fd0\u77f3\u4e4b\u95e8  \u8d1f\u8377\u9886\u57df\u7684\u65e2\u89c6\u611f";
        const string firstHtml = "<html><body><div>No Result</div></body></html>";
        const string secondHtml = """
                                <html>
                                  <body>
                                    <ul>
                                      <li>
                                        <a href="/Home/Bangumi/1234">
                                          <span class="an-title">Steins;Gate Season 1 2025</span>
                                        </a>
                                      </li>
                                    </ul>
                                  </body>
                                </html>
                                """;

        var requestUris = new List<string>();
        var requestQueries = new List<string>();
        var callCount = 0;

        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                requestUris.Add(request.RequestUri?.ToString() ?? string.Empty);
                requestQueries.Add(request.RequestUri?.Query ?? string.Empty);
                callCount++;

                var content = callCount == 1 ? firstHtml : secondHtml;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(content, Encoding.UTF8, "text/html")
                };
            });

        var sut = CreateSut(handlerMock);

        // Act
        var result = await sut.SearchAnimeAsync(title);

        // Assert
        result.Should().NotBeNull();
        result!.Seasons.Should().ContainSingle();
        result.Seasons[0].MikanBangumiId.Should().Be("1234");

        requestUris.Should().HaveCount(2);
        requestQueries[0].Should().Contain("++");
        requestQueries[1].Should().Contain("+");
        requestQueries[1].Should().NotContain("++");
    }

    [Fact]
    public async Task SearchAnimeAsync_WhenDefaultQueryReturnsResult_DoesNotRetryPlusJoinedQuery()
    {
        // Arrange
        const string title = "\u547d\u8fd0\u77f3\u4e4b\u95e8 \u8d1f\u8377\u9886\u57df\u7684\u65e2\u89c6\u611f";
        const string html = """
                            <html>
                              <body>
                                <ul>
                                  <li>
                                    <a href="/Home/Bangumi/2233">
                                      <span class="an-title">Steins;Gate Season 1 2025</span>
                                    </a>
                                  </li>
                                </ul>
                              </body>
                            </html>
                            """;

        var requestUris = new List<string>();
        var requestQueries = new List<string>();

        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                requestUris.Add(request.RequestUri?.ToString() ?? string.Empty);
                requestQueries.Add(request.RequestUri?.Query ?? string.Empty);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(html, Encoding.UTF8, "text/html")
                };
            });

        var sut = CreateSut(handlerMock);

        // Act
        var result = await sut.SearchAnimeAsync(title);

        // Assert
        result.Should().NotBeNull();
        result!.Seasons.Should().ContainSingle();
        requestUris.Should().HaveCount(1);
        requestQueries[0].Should().Contain("+");
    }

    [Fact]
    public async Task SearchAnimeAsync_WhenOnlyEpisodeRowsExist_ReturnsSearchPseudoSeason()
    {
        // Arrange
        const string title = "\u547d\u8fd0\u77f3\u4e4b\u95e8 \u8d1f\u8377\u9886\u57df\u7684\u65e2\u89c6\u611f";
        const string html = """
                            <html>
                              <body>
                                <table>
                                  <tbody>
                                    <tr class="js-search-results-row">
                                      <td>
                                        <a href="/Home/Episode/66bbc2943d86ca8ebf509c247e145cb4f5b78960">Episode Link</a>
                                      </td>
                                    </tr>
                                  </tbody>
                                </table>
                              </body>
                            </html>
                            """;

        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, Encoding.UTF8, "text/html")
            });

        var sut = CreateSut(handlerMock);

        // Act
        var result = await sut.SearchAnimeAsync(title);

        // Assert
        result.Should().NotBeNull();
        result!.Seasons.Should().ContainSingle();
        result.Seasons[0].SeasonName.Should().Be("Search Results");
        result.Seasons[0].MikanBangumiId.Should().Be($"search:{title}");
    }

    [Fact]
    public void BuildRssUrl_WhenPseudoSearchIdProvided_UsesRssSearchEndpoint()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        var sut = CreateSut(handlerMock);

        // Act
        var url = sut.BuildRssUrl("search:\u547d\u8fd0\u77f3\u4e4b\u95e8 \u8d1f\u8377\u9886\u57df\u7684\u65e2\u89c6\u611f");

        // Assert
        url.Should().Be("RSS/Search?searchstr=%E5%91%BD%E8%BF%90%E7%9F%B3%E4%B9%8B%E9%97%A8+%E8%B4%9F%E8%8D%B7%E9%A2%86%E5%9F%9F%E7%9A%84%E6%97%A2%E8%A7%86%E6%84%9F");
    }

    [Fact]
    public async Task GetParsedFeedAsync_WhenFreshCacheExists_ReturnsCacheWithoutNetworkCall()
    {
        // Arrange
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var dbOptions = new DbContextOptionsBuilder<AnimeDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new AnimeDbContext(dbOptions);
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.MikanFeedCaches.Add(new MikanFeedCacheEntity
        {
            MikanBangumiId = "3821",
            SeasonName = "Cached Feed",
            LatestEpisode = 4,
            LatestPublishedAt = DateTime.UtcNow,
            LatestTitle = "Cached Episode",
            EpisodeOffset = 0,
            UpdatedAt = DateTime.UtcNow
        });
        dbContext.MikanFeedItems.Add(new MikanFeedItemEntity
        {
            MikanBangumiId = "3821",
            Title = "Cached Episode",
            TorrentHash = "HASHCACHED001",
            TorrentUrl = "https://example.com/cached.torrent",
            MagnetLink = "magnet:?xt=urn:btih:HASHCACHED001",
            CanDownload = true,
            FileSize = 1234,
            FormattedSize = "1.2 KB",
            PublishedAt = DateTime.UtcNow,
            Resolution = "1080P",
            Subgroup = "GroupA",
            SubtitleType = "简日内嵌",
            Episode = 4,
            IsCollection = false
        });
        await dbContext.SaveChangesAsync();

        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        var sut = CreateSut(handlerMock, dbContext);

        // Act
        var response = await sut.GetParsedFeedAsync("3821");

        // Assert
        response.SeasonName.Should().Be("Cached Feed");
        response.Items.Should().ContainSingle();
        response.Items[0].Title.Should().Be("Cached Episode");
    }

    [Fact]
    public async Task GetParsedFeedAsync_WhenCacheExpired_RefreshesFromNetworkAndPersists()
    {
        // Arrange
        const string xml = """
                           <rss version="2.0">
                             <channel>
                               <title>Live Feed</title>
                               <item>
                                 <guid isPermaLink="false">live-guid</guid>
                                 <title>[GroupB] Live Anime - 05 [1080P]</title>
                                 <link>https://mikan.tangbai.cc/Home/Episode/live</link>
                                 <pubDate>2026-01-01T12:00:00+00:00</pubDate>
                                 <enclosure type="application/x-bittorrent" length="2048" url="https://mikan.tangbai.cc/Download/live.torrent" />
                                 <torrent xmlns="https://mikanani.me/0.1/">
                                   <magnetURI>magnet:?xt=urn:btih:0123456789ABCDEF0123456789ABCDEF01234567</magnetURI>
                                 </torrent>
                               </item>
                             </channel>
                           </rss>
                           """;

        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var dbOptions = new DbContextOptionsBuilder<AnimeDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new AnimeDbContext(dbOptions);
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.MikanFeedCaches.Add(new MikanFeedCacheEntity
        {
            MikanBangumiId = "3849",
            SeasonName = "Old Feed",
            UpdatedAt = DateTime.UtcNow.AddHours(-3)
        });
        dbContext.MikanFeedItems.Add(new MikanFeedItemEntity
        {
            MikanBangumiId = "3849",
            Title = "Old Item",
            TorrentHash = "OLDHASH001",
            TorrentUrl = "https://example.com/old.torrent",
            MagnetLink = "magnet:?xt=urn:btih:OLDHASH001",
            CanDownload = true,
            FileSize = 1024,
            FormattedSize = "1.0 KB",
            PublishedAt = DateTime.UtcNow.AddHours(-4),
            Episode = 1
        });
        await dbContext.SaveChangesAsync();

        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(xml, Encoding.UTF8, "application/xml")
            });

        var sut = CreateSut(handlerMock, dbContext);

        // Act
        var response = await sut.GetParsedFeedAsync("3849");

        // Assert
        response.SeasonName.Should().Be("Live Feed");
        response.Items.Should().ContainSingle(i => i.Title.Contains("Live Anime"));

        var cache = await dbContext.MikanFeedCaches.AsNoTracking().SingleAsync(c => c.MikanBangumiId == "3849");
        var cachedItems = await dbContext.MikanFeedItems
            .AsNoTracking()
            .Where(i => i.MikanBangumiId == "3849")
            .ToListAsync();
        cache.SeasonName.Should().Be("Live Feed");
        cachedItems.Should().ContainSingle(i => i.Title.Contains("Live Anime"));
    }

    [Fact]
    public async Task GetParsedFeedAsync_WhenRefreshFailsAndHasCache_FallsBackToStaleCache()
    {
        // Arrange
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var dbOptions = new DbContextOptionsBuilder<AnimeDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new AnimeDbContext(dbOptions);
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.MikanFeedCaches.Add(new MikanFeedCacheEntity
        {
            MikanBangumiId = "9999",
            SeasonName = "Stale Feed",
            UpdatedAt = DateTime.UtcNow.AddHours(-4)
        });
        dbContext.MikanFeedItems.Add(new MikanFeedItemEntity
        {
            MikanBangumiId = "9999",
            Title = "Stale Item",
            TorrentHash = "STALEHASH001",
            TorrentUrl = "https://example.com/stale.torrent",
            MagnetLink = "magnet:?xt=urn:btih:STALEHASH001",
            CanDownload = true,
            FileSize = 512,
            FormattedSize = "512 B",
            PublishedAt = DateTime.UtcNow.AddHours(-5),
            Episode = 2
        });
        await dbContext.SaveChangesAsync();

        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var sut = CreateSut(handlerMock, dbContext);

        // Act
        var response = await sut.GetParsedFeedAsync("9999");

        // Assert
        response.SeasonName.Should().Be("Stale Feed");
        response.Items.Should().ContainSingle(i => i.Title == "Stale Item");
    }

    private static MikanClient CreateSut(Mock<HttpMessageHandler> handlerMock, AnimeDbContext? dbContext = null)
    {
        var httpClient = new HttpClient(handlerMock.Object);
        var config = Options.Create(new MikanConfiguration
        {
            BaseUrl = "https://mikan.tangbai.cc",
            TimeoutSeconds = 30,
            FeedCacheTtlMinutes = 10
        });

        var logger = new Mock<ILogger<MikanClient>>();
        var parser = new TorrentTitleParser();
        return new MikanClient(httpClient, logger.Object, config, parser, dbContext);
    }
}
