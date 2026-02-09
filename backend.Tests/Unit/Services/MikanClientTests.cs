using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
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

    private static MikanClient CreateSut(Mock<HttpMessageHandler> handlerMock)
    {
        var httpClient = new HttpClient(handlerMock.Object);
        var config = Options.Create(new MikanConfiguration
        {
            BaseUrl = "https://mikan.tangbai.cc",
            TimeoutSeconds = 30
        });

        var logger = new Mock<ILogger<MikanClient>>();
        var parser = new TorrentTitleParser();
        return new MikanClient(httpClient, logger.Object, config, parser);
    }
}
