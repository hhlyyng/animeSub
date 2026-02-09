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
}
