using FluentAssertions;
using backend.Services.Utilities;

namespace backend.Tests.Unit.Services;

/// <summary>
/// Unit tests for TitleCleaner - testing season suffix removal for anime titles
/// </summary>
public class TitleCleanerTests
{
    #region Chinese Pattern Tests

    [Theory]
    [InlineData("魔都精兵的奴隶 第二季", "魔都精兵的奴隶", true)]
    [InlineData("进击的巨人 第四季", "进击的巨人", true)]
    [InlineData("鬼灭之刃 第2季", "鬼灭之刃", true)]
    [InlineData("咒术回战 第三季", "咒术回战", true)]
    public void RemoveSeasonSuffix_ChineseSeasonPattern_RemovesSuffix(string input, string expectedTitle, bool expectedCleaned)
    {
        // Act
        var (cleanedTitle, wasCleaned) = TitleCleaner.RemoveSeasonSuffix(input);

        // Assert
        cleanedTitle.Should().Be(expectedTitle);
        wasCleaned.Should().Be(expectedCleaned);
    }

    [Theory]
    [InlineData("进击的巨人 最终季", "进击的巨人", true)]
    [InlineData("鬼灭之刃 续篇", "鬼灭之刃", true)]
    [InlineData("火影忍者 续集", "火影忍者", true)]
    public void RemoveSeasonSuffix_ChineseSpecialPatterns_RemovesSuffix(string input, string expectedTitle, bool expectedCleaned)
    {
        // Act
        var (cleanedTitle, wasCleaned) = TitleCleaner.RemoveSeasonSuffix(input);

        // Assert
        cleanedTitle.Should().Be(expectedTitle);
        wasCleaned.Should().Be(expectedCleaned);
    }

    #endregion

    #region Japanese Pattern Tests

    [Theory]
    [InlineData("Re:ゼロから始める異世界生活 3期", "Re:ゼロから始める異世界生活", true)]
    [InlineData("僕のヒーローアカデミア 第5期", "僕のヒーローアカデミア", true)]
    [InlineData("ソードアート・オンライン シーズン3", "ソードアート・オンライン", true)]
    public void RemoveSeasonSuffix_JapaneseSeasonPattern_RemovesSuffix(string input, string expectedTitle, bool expectedCleaned)
    {
        // Act
        var (cleanedTitle, wasCleaned) = TitleCleaner.RemoveSeasonSuffix(input);

        // Assert
        cleanedTitle.Should().Be(expectedTitle);
        wasCleaned.Should().Be(expectedCleaned);
    }

    [Theory]
    [InlineData("呪術廻戦 セカンドシーズン", "呪術廻戦", true)]
    [InlineData("東京リベンジャーズ サードシーズン", "東京リベンジャーズ", true)]
    public void RemoveSeasonSuffix_JapaneseNamedSeasons_RemovesSuffix(string input, string expectedTitle, bool expectedCleaned)
    {
        // Act
        var (cleanedTitle, wasCleaned) = TitleCleaner.RemoveSeasonSuffix(input);

        // Assert
        cleanedTitle.Should().Be(expectedTitle);
        wasCleaned.Should().Be(expectedCleaned);
    }

    #endregion

    #region English Pattern Tests

    [Theory]
    [InlineData("Attack on Titan Season 4", "Attack on Titan", true)]
    [InlineData("Demon Slayer Season 2", "Demon Slayer", true)]
    [InlineData("My Hero Academia S6", "My Hero Academia", true)]
    public void RemoveSeasonSuffix_EnglishSeasonPattern_RemovesSuffix(string input, string expectedTitle, bool expectedCleaned)
    {
        // Act
        var (cleanedTitle, wasCleaned) = TitleCleaner.RemoveSeasonSuffix(input);

        // Assert
        cleanedTitle.Should().Be(expectedTitle);
        wasCleaned.Should().Be(expectedCleaned);
    }

    [Theory]
    [InlineData("Demon Slayer Part 2", "Demon Slayer", true)]
    [InlineData("Attack on Titan Part 3", "Attack on Titan", true)]
    public void RemoveSeasonSuffix_EnglishPartPattern_RemovesSuffix(string input, string expectedTitle, bool expectedCleaned)
    {
        // Act
        var (cleanedTitle, wasCleaned) = TitleCleaner.RemoveSeasonSuffix(input);

        // Assert
        cleanedTitle.Should().Be(expectedTitle);
        wasCleaned.Should().Be(expectedCleaned);
    }

    [Theory]
    [InlineData("Mob Psycho 100 2nd Season", "Mob Psycho 100", true)]
    [InlineData("One Punch Man 3rd Season", "One Punch Man", true)]
    public void RemoveSeasonSuffix_EnglishOrdinalPattern_RemovesSuffix(string input, string expectedTitle, bool expectedCleaned)
    {
        // Act
        var (cleanedTitle, wasCleaned) = TitleCleaner.RemoveSeasonSuffix(input);

        // Assert
        cleanedTitle.Should().Be(expectedTitle);
        wasCleaned.Should().Be(expectedCleaned);
    }

    [Fact]
    public void RemoveSeasonSuffix_TheFinalSeason_RemovesSuffix()
    {
        // Arrange
        var input = "Attack on Titan The Final Season";

        // Act
        var (cleanedTitle, wasCleaned) = TitleCleaner.RemoveSeasonSuffix(input);

        // Assert
        cleanedTitle.Should().Be("Attack on Titan");
        wasCleaned.Should().BeTrue();
    }

    #endregion

    #region Roman Numeral Tests

    [Theory]
    [InlineData("Overlord II", "Overlord", true)]
    [InlineData("Overlord III", "Overlord", true)]
    [InlineData("Overlord IV", "Overlord", true)]
    public void RemoveSeasonSuffix_RomanNumerals_RemovesSuffix(string input, string expectedTitle, bool expectedCleaned)
    {
        // Act
        var (cleanedTitle, wasCleaned) = TitleCleaner.RemoveSeasonSuffix(input);

        // Assert
        cleanedTitle.Should().Be(expectedTitle);
        wasCleaned.Should().Be(expectedCleaned);
    }

    #endregion

    #region No Cleaning Required Tests

    [Theory]
    [InlineData("葬送のフリーレン", "葬送のフリーレン", false)]
    [InlineData("Frieren: Beyond Journey's End", "Frieren: Beyond Journey's End", false)]
    [InlineData("SPY×FAMILY", "SPY×FAMILY", false)]
    [InlineData("ぼっち・ざ・ろっく！", "ぼっち・ざ・ろっく！", false)]
    public void RemoveSeasonSuffix_NoSeasonSuffix_ReturnsOriginal(string input, string expectedTitle, bool expectedCleaned)
    {
        // Act
        var (cleanedTitle, wasCleaned) = TitleCleaner.RemoveSeasonSuffix(input);

        // Assert
        cleanedTitle.Should().Be(expectedTitle);
        wasCleaned.Should().Be(expectedCleaned);
    }

    #endregion

    #region Exclusion List Tests

    [Theory]
    [InlineData("86 EIGHTY-SIX", "86 EIGHTY-SIX", false)]
    [InlineData("86", "86", false)]
    [InlineData("Final Fantasy VII", "Final Fantasy VII", false)]
    [InlineData("Final Fantasy VII Remake", "Final Fantasy VII Remake", false)]
    public void RemoveSeasonSuffix_ExcludedTitles_ReturnsOriginal(string input, string expectedTitle, bool expectedCleaned)
    {
        // Act
        var (cleanedTitle, wasCleaned) = TitleCleaner.RemoveSeasonSuffix(input);

        // Assert
        cleanedTitle.Should().Be(expectedTitle);
        wasCleaned.Should().Be(expectedCleaned);
    }

    #endregion

    #region Edge Cases

    [Theory]
    [InlineData("", "", false)]
    [InlineData("  ", "  ", false)]
    [InlineData(null, null, false)]
    public void RemoveSeasonSuffix_EmptyOrNull_ReturnsInput(string? input, string? expectedTitle, bool expectedCleaned)
    {
        // Act
        var (cleanedTitle, wasCleaned) = TitleCleaner.RemoveSeasonSuffix(input!);

        // Assert
        cleanedTitle.Should().Be(expectedTitle);
        wasCleaned.Should().Be(expectedCleaned);
    }

    [Fact]
    public void RemoveSeasonSuffix_TitleWithExtraSpaces_TrimsResult()
    {
        // Arrange
        var input = "  進撃の巨人 第二季  ";

        // Act
        var (cleanedTitle, wasCleaned) = TitleCleaner.RemoveSeasonSuffix(input);

        // Assert
        cleanedTitle.Should().Be("進撃の巨人");
        wasCleaned.Should().BeTrue();
    }

    [Fact]
    public void RemoveSeasonSuffix_CaseInsensitiveEnglish_Works()
    {
        // Arrange
        var inputs = new[] { "Attack on Titan SEASON 4", "Attack on Titan season 4", "Attack on Titan Season 4" };

        foreach (var input in inputs)
        {
            // Act
            var (cleanedTitle, wasCleaned) = TitleCleaner.RemoveSeasonSuffix(input);

            // Assert
            cleanedTitle.Should().Be("Attack on Titan");
            wasCleaned.Should().BeTrue();
        }
    }

    #endregion

    #region HasSeasonSuffix Tests

    [Theory]
    [InlineData("魔都精兵的奴隶 第二季", true)]
    [InlineData("Attack on Titan Season 4", true)]
    [InlineData("Overlord III", true)]
    [InlineData("葬送のフリーレン", false)]
    [InlineData("86 EIGHTY-SIX", false)]
    public void HasSeasonSuffix_ReturnsExpected(string input, bool expected)
    {
        // Act
        var result = TitleCleaner.HasSeasonSuffix(input);

        // Assert
        result.Should().Be(expected);
    }

    #endregion
}
