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
    // Bug 7: S02 - 23 (two-digit season + separator + episode) must not be treated as a range
    [InlineData("[Lilith-Raws] \u5492\u672f\u56de\u6218 / Jujutsu Kaisen S02 - 23 [Baha][WebDL 1080p AVC AAC][CHT]", 23)]
    [InlineData("[jibaketa\u5408\u6210&\u97f3\u9891\u538b\u5236][\u4ee3\u7406\u5546\u7ca4\u8bed]\u5492\u672f\u56de\u6218 \u7b2c\u4e8c\u5b63 / Jujutsu Kaisen S2 - 23 END [\u7ca4\u65e5\u53cc\u8bed+\u5185\u5c01\u7e41\u4f53\u4e2d\u6587\u5b57\u5e55](WEB 1920x1080 AVC AACx2 SRT Ani-One CHT)", 23)]
    [InlineData("[Group] Anime Name S02E05 [1080P][CHS]", 5)]
    [InlineData("[Group] Anime Name EP12 [720p]", 12)]
    [InlineData("[Group] Anime Name \u7b2c13\u8bdd [1080p]", 13)]
    // Bug 1: episode > 500 (One Piece 1021)
    [InlineData("#\u6d77\u8d3c\u738b# \u5178\u85cf\u7248 \u7b2c1021\u96c6", 1021)]
    // Bug 2: title number vs. real episode
    [InlineData("[ANi] \u8d85\u8d85\u8d85\u8d85\u8d85\u559c\u6b22\u4f60\u7684 100 \u4e2a\u5973\u670b\u53cb - 13 [1080P][\u7e41\u65e5\u5185\u5d4c]", 13)]
    [InlineData("[ANi] Mob Psycho 100 S3 - \u8defRen\u8d85\u80fd100 \u7b2c\u4e09\u5b63 - 01 [1080p]", 1)]
    [InlineData("\u3010WOLF\u5b57\u5e55\u7ec4\u3011[\u67d0\u5408\u96c6] Season2\u3011\u3010\u7b2c07&08\u8bdd\u3011", 8)]
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

    [Fact]
    public void ParseTitle_ShouldNormalizeAi2160pToAi4k()
    {
        // Arrange
        var title = "[\u6cb8\u73ed\u4e9a\u9a6c\u5236\u4f5c\u7ec4] \u5730\u72f1\u4e50 \u7b2c\u4e8c\u5b63 - 02 [CR WebRip AI2160p HEVC OPUS][\u7b80\u7e41\u5185\u5c01\u5b57\u5e55]";

        // Act
        var parsed = _sut.ParseTitle(title);

        // Assert
        parsed.Resolution.Should().Be("AI4K");
    }

    [Theory]
    [InlineData("[\u9ed2\u30cd\u30ba\u30df\u305f\u3061] \u5492\u672f\u56de\u6218 \u6b7b\u706d\u56de\u6e38 \u524d\u7bc7 / Jujutsu Kaisen: Shimetsu Kaiyuu - Zenpen - 49 (CR 1920x1080 AVC AAC MKV)", "1080p")]
    [InlineData("[Group] Anime Name - 05 (WEB 1280*720 AVC AAC MP4)", "720p")]
    [InlineData("[Group] Anime Name - 05 (WEB 1280\u00d7720 AVC AAC MP4)", "720p")]
    public void ParseTitle_ShouldExtractResolution_FromDimensionPattern(string title, string expectedResolution)
    {
        // Act
        var parsed = _sut.ParseTitle(title);

        // Assert
        parsed.Resolution.Should().Be(expectedResolution);
    }

    // Bug 3: multiple resolutions — pick the highest
    [Fact]
    public void ParseTitle_ShouldPickHighestResolution_WhenMultiplePresent()
    {
        var title = "[Group] Anime - 05 (MP4.720P MKV.720p.1080p)";

        var parsed = _sut.ParseTitle(title);

        parsed.Resolution.Should().Be("1080p");
    }

    [Theory]
    [InlineData("[ANi] You and I Are Polar Opposites - 05 [1080P][CHT]", "ANi")]
    [InlineData("\u3010MoeSub\u3011 Anime Title - 04 [1080p]", "MoeSub")]
    [InlineData("\u3010JYH-RIP\u3011 Anime Title \u7b2c04\u96c6 GB_CN AV1_opus 1080p", "JYH-RIP")]
    // Bug 6: prefix noise before bracket
    [InlineData("\u641c\u8fd0 [OPFans\u67ab\u96ea\u52a8\u6f2b] \u67d0\u756a", "OPFans\u67ab\u96ea\u52a8\u6f2b")]
    public void ParseTitle_ShouldExtractSubgroup_ForHalfWidthAndFullWidthBrackets(string title, string expectedSubgroup)
    {
        // Act
        var parsed = _sut.ParseTitle(title);

        // Assert
        parsed.Subgroup.Should().Be(expectedSubgroup);
    }

    [Theory]
    [InlineData("[\u8c4c\u8c46\u5b57\u5e55\u7ec4&\u98ce\u4e4b\u5723\u6bbf&LoliHouse] \u5492\u672f\u56de\u6218 / Jujutsu Kaisen - 53 [WebRip 1080p HEVC-10bit AAC][\u7b80\u7e41\u5916\u6302\u5b57\u5e55]", "\u7b80\u7e41\u5916\u6302")]
    [InlineData("\u3010\u8c4c\u8c46\u5b57\u5e55\u7ec4&\u98ce\u4e4b\u5723\u6bbf\u5b57\u5e55\u7ec4\u301107\u6708\u65b0\u756a[\u5492\u672f\u56de\u6218 / Jujutsu_Kaisen][53][\u7b80\u4f53][1080P][MP4]", "\u7b80\u4f53")]
    [InlineData("\u3010\u8c4c\u8c46\u5b57\u5e55\u7ec4&\u98ce\u4e4b\u5723\u6bbf\u5b57\u5e55\u7ec4\u301107\u6708\u65b0\u756a[\u5492\u672f\u56de\u6218 / Jujutsu_Kaisen][53][\u7e41\u4f53][1080P][MP4]", "\u7e41\u4f53")]
    [InlineData("[\u7eff\u8336\u5b57\u5e55\u7ec4] \u5492\u672f\u56de\u6218 / Jujutsu Kaisen [51][WebRip][1080p][\u7e41\u65e5\u5185\u5d4c]", "\u7e41\u65e5\u5185\u5d4c")]
    // Bug 4: 简繁日内封
    [InlineData("[\u8c4c\u8c46&\u98ce\u4e4b\u5723\u6bbf] \u67d0\u52a8\u753b [01][\u7b80\u7e41\u65e5\u5185\u5c01]", "\u7b80\u7e41\u65e5\u5185\u5c01")]
    // Bug 4: CHT+CHS → 简繁
    [InlineData("[Group] Anime [CHT CHS][1080p]", "\u7b80\u7e41")]
    public void ParseTitle_ShouldExtractDetailedSubtitleType(string title, string expectedSubtitleType)
    {
        // Act
        var parsed = _sut.ParseTitle(title);

        // Assert
        parsed.SubtitleType.Should().Be(expectedSubtitleType);
    }

    // Bug 5: range episode → IsCollection = true
    [Fact]
    public void ParseTitle_ShouldDetectRangeAsCollection()
    {
        var title = "\u7b2c841-847\u8bdd";

        var parsed = _sut.ParseTitle(title);

        parsed.IsCollection.Should().BeTrue();
    }

    // Bug 5: single episode with 典藏版 → IsCollection = false
    [Fact]
    public void ParseTitle_ShouldNotTreatSingleEpisodeCollectorEditionAsCollection()
    {
        var title = "\u5178\u85cf\u7248 \u7b2c1021\u96c6";

        var parsed = _sut.ParseTitle(title);

        parsed.IsCollection.Should().BeFalse();
    }

    // Bug 7: "Part 1+2" batch + x.264 codec mistaken for ep 264
    [Fact]
    public void ParseTitle_ShouldTreatPartPlusRangeAsBatchCollection_AndNotExtractCodecAsEpisode()
    {
        var title = "[Moozzi2] \u8fdb\u51fb\u7684\u5de8\u4eba\u7b2c\u4e09\u5b63 Shingeki no Kyojin S3 Part 1+2 (BD 1920x1080 x.264 Flac)";

        var parsed = _sut.ParseTitle(title);

        parsed.IsCollection.Should().BeTrue();
        parsed.Episode.Should().BeNull();
    }
}
