using FluentAssertions;
using backend.Services.Utilities;

namespace backend.Tests.Unit.Services;

public class TitleLanguageResolverTests
{
    [Fact]
    public void ResolveFromName_WhenJapaneseKana_FillsJapaneseTitle()
    {
        var result = TitleLanguageResolver.ResolveFromName("ぼっち・ざ・ろっく!");

        result.jpTitle.Should().Be("ぼっち・ざ・ろっく!");
        result.chTitle.Should().BeEmpty();
        result.enTitle.Should().BeEmpty();
    }

    [Fact]
    public void ResolveFromName_WhenHanOnly_FillsChineseAndJapaneseFallback()
    {
        var result = TitleLanguageResolver.ResolveFromName("中国奇谭");

        result.chTitle.Should().Be("中国奇谭");
        result.jpTitle.Should().Be("中国奇谭");
        result.enTitle.Should().BeEmpty();
    }

    [Fact]
    public void ResolveFromName_WhenLatin_FillsEnglishTitle()
    {
        var result = TitleLanguageResolver.ResolveFromName("Yao-Chinese Folktales");

        result.enTitle.Should().Be("Yao-Chinese Folktales");
        result.jpTitle.Should().BeEmpty();
        result.chTitle.Should().BeEmpty();
    }

    [Fact]
    public void ResolveFromName_DoesNotOverrideExistingFields()
    {
        var result = TitleLanguageResolver.ResolveFromName(
            "Yao-Chinese Folktales",
            jpTitle: "既有日文",
            chTitle: "既有中文",
            enTitle: "Existing English");

        result.jpTitle.Should().Be("既有日文");
        result.chTitle.Should().Be("既有中文");
        result.enTitle.Should().Be("Existing English");
    }
}
