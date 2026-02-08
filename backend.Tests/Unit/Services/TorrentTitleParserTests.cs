using FluentAssertions;
using backend.Services.Implementations;

namespace backend.Tests.Unit.Services;

public class TorrentTitleParserTests
{
    private readonly TorrentTitleParser _sut = new();

    [Theory]
    [InlineData("[\u685c\u90fd\u5b57\u5e55\u7ec4] \u6709\u6816\u5ddd\u70bc\u539f\u6765\u662f\u5973\u5b69\u5b50\u554a\u3002 / Arisugawa Ren tte Honto wa Onna Nanda yo ne. [03][1080P][\u7e41\u4f53\u5185\u5d4c]", 3)]
    [InlineData("[\u685c\u90fd\u5b57\u5e55\u7ec4] \u6709\u6816\u5ddd\u70bc\u539f\u6765\u662f\u5973\u5b69\u5b50\u554a\u3002 / Arisugawa Ren tte Honto wa Onna Nanda yo ne. [01][1080p][\u7b80\u7e41\u5185\u5c01]", 1)]
    [InlineData("[\u9ed2\u30cd\u30ba\u30df\u305f\u3061] \u6709\u6816\u5ddd\u70bc\u5176\u5b9e\u662f\u4e2a\u5973\u751f\u5427\u3002 / Arisugawa Ren tte Honto wa Onna Nanda yo ne. - 04 (ABEMA 1920x1080 AVC AAC MP4)", 4)]
    [InlineData("[Group] Anime Name S02E05 [1080P][CHS]", 5)]
    [InlineData("[Group] Anime Name EP12 [720p]", 12)]
    [InlineData("[Group] Anime Name \u7b2c13\u8bdd [1080p]", 13)]
    public void ParseTitle_ShouldExtractEpisode_ForSupportedPatterns(string title, int expectedEpisode)
    {
        // Act
        var parsed = _sut.ParseTitle(title);

        // Assert
        parsed.Episode.Should().Be(expectedEpisode);
    }

    [Fact]
    public void ParseTitle_ShouldNotTreatResolutionAsEpisode()
    {
        // Arrange
        var title = "[Group] Anime Name [1080P][\u7b80\u4f53\u5185\u5d4c]";

        // Act
        var parsed = _sut.ParseTitle(title);

        // Assert
        parsed.Episode.Should().BeNull();
        parsed.Resolution.Should().Be("1080p");
    }

    [Theory]
    [InlineData("[ANi] You and I Are Polar Opposites - 05 [1080P][CHT]", "ANi")]
    [InlineData("\u3010MoeSub\u3011 Anime Title - 04 [1080p]", "MoeSub")]
    [InlineData("\u3010JYH-RIP\u3011 Anime Title \u7b2c04\u96c6 GB_CN AV1_opus 1080p", "JYH-RIP")]
    public void ParseTitle_ShouldExtractSubgroup_ForHalfWidthAndFullWidthBrackets(string title, string expectedSubgroup)
    {
        // Act
        var parsed = _sut.ParseTitle(title);

        // Assert
        parsed.Subgroup.Should().Be(expectedSubgroup);
    }
}
