using System.Text.RegularExpressions;

namespace backend.Services.Utilities;

/// <summary>
/// Resolves where a source title should be placed (JP/CH/EN) based on script detection.
/// </summary>
public static class TitleLanguageResolver
{
    private static readonly Regex KanaRegex = new(@"[\p{IsHiragana}\p{IsKatakana}]", RegexOptions.Compiled);
    private static readonly Regex HanRegex = new(@"\p{IsCJKUnifiedIdeographs}", RegexOptions.Compiled);
    private static readonly Regex LatinRegex = new(@"[A-Za-z]", RegexOptions.Compiled);

    public static (string jpTitle, string chTitle, string enTitle) ResolveFromName(
        string? name,
        string? jpTitle = null,
        string? chTitle = null,
        string? enTitle = null)
    {
        var resolvedJp = Normalize(jpTitle);
        var resolvedCh = Normalize(chTitle);
        var resolvedEn = Normalize(enTitle);
        var normalizedName = Normalize(name);

        if (string.IsNullOrEmpty(normalizedName))
        {
            return (resolvedJp, resolvedCh, resolvedEn);
        }

        var hasKana = KanaRegex.IsMatch(normalizedName);
        var hanCount = CountMatches(HanRegex, normalizedName);
        var latinCount = CountMatches(LatinRegex, normalizedName);

        if (hasKana)
        {
            resolvedJp = FillIfEmpty(resolvedJp, normalizedName);
            return (resolvedJp, resolvedCh, resolvedEn);
        }

        if (hanCount > 0 && latinCount == 0)
        {
            // Han-only titles are ambiguous between Chinese/Japanese.
            // Fill both as fallback to avoid losing display/searchability.
            resolvedCh = FillIfEmpty(resolvedCh, normalizedName);
            resolvedJp = FillIfEmpty(resolvedJp, normalizedName);
            return (resolvedJp, resolvedCh, resolvedEn);
        }

        if (latinCount > 0 && hanCount == 0)
        {
            resolvedEn = FillIfEmpty(resolvedEn, normalizedName);
            return (resolvedJp, resolvedCh, resolvedEn);
        }

        if (hanCount > 0 && latinCount > 0)
        {
            if (hanCount >= latinCount)
            {
                resolvedCh = FillIfEmpty(resolvedCh, normalizedName);
            }
            else
            {
                resolvedEn = FillIfEmpty(resolvedEn, normalizedName);
            }

            return (resolvedJp, resolvedCh, resolvedEn);
        }

        resolvedEn = FillIfEmpty(resolvedEn, normalizedName);
        return (resolvedJp, resolvedCh, resolvedEn);
    }

    private static string FillIfEmpty(string current, string candidate)
    {
        return string.IsNullOrEmpty(current) ? candidate : current;
    }

    private static int CountMatches(Regex regex, string input)
    {
        return regex.Matches(input).Count;
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
