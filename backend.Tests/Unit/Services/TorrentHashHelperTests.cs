using FluentAssertions;
using backend.Services.Utils;

namespace backend.Tests.Unit.Services;

public class TorrentHashHelperTests
{
    [Fact]
    public void ResolveHash_WhenMagnetContainsBase32Btih_ReturnsNormalizedHex()
    {
        // Arrange
        var magnet = "magnet:?xt=urn:btih:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA&dn=test";

        // Act
        var hash = TorrentHashHelper.ResolveHash(magnet);

        // Assert
        hash.Should().Be("0000000000000000000000000000000000000000");
    }

    [Fact]
    public void ResolveHash_WhenSourceContainsHexHash_ReturnsUppercaseHash()
    {
        // Arrange
        var source = "https://example.com/file/abcdefabcdefabcdefabcdefabcdefabcdefabcd.torrent";

        // Act
        var hash = TorrentHashHelper.ResolveHash(source);

        // Assert
        hash.Should().Be("ABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCD");
    }

    [Fact]
    public void ResolveHash_WhenNoValidHashExists_ReturnsNull()
    {
        // Arrange
        var source = "magnet:?dn=no-hash-here";

        // Act
        var hash = TorrentHashHelper.ResolveHash(source);

        // Assert
        hash.Should().BeNull();
    }
}
