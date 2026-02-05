using System.Text.RegularExpressions;

namespace backend.Services.Utilities
{
    /// <summary>
    /// Utility class for cleaning anime titles by removing season suffixes.
    /// Supports Chinese, Japanese, English patterns and Roman numerals.
    /// </summary>
    public static class TitleCleaner
    {
        /// <summary>
        /// Titles that should not be cleaned to avoid false positives.
        /// These contain patterns that look like season suffixes but are part of the actual title.
        /// </summary>
        private static readonly HashSet<string> ExcludedTitles = new(StringComparer.OrdinalIgnoreCase)
        {
            "86 EIGHTY-SIX",
            "86",
            "Final Fantasy VII",
            "Final Fantasy VII Remake",
        };

        /// <summary>
        /// Season patterns to remove from titles.
        /// Each tuple contains the regex pattern and a description for debugging.
        /// </summary>
        private static readonly (Regex Pattern, string Description)[] SeasonPatterns = new[]
        {
            // Chinese patterns
            (new Regex(@"\s*第[一二三四五六七八九十\d]+季\s*$", RegexOptions.Compiled), "第N季"),
            (new Regex(@"\s*最终季\s*$", RegexOptions.Compiled), "最终季"),
            (new Regex(@"\s*续篇\s*$", RegexOptions.Compiled), "续篇"),
            (new Regex(@"\s*续集\s*$", RegexOptions.Compiled), "续集"),

            // Japanese patterns
            (new Regex(@"\s*シーズン\s*\d+\s*$", RegexOptions.Compiled), "シーズンN"),
            (new Regex(@"\s*第?\d+期\s*$", RegexOptions.Compiled), "N期/第N期"),
            (new Regex(@"\s*セカンドシーズン\s*$", RegexOptions.Compiled), "セカンドシーズン"),
            (new Regex(@"\s*サードシーズン\s*$", RegexOptions.Compiled), "サードシーズン"),

            // English patterns
            (new Regex(@"\s*Season\s*\d+\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Season N"),
            (new Regex(@"\s*S\d+\s*$", RegexOptions.Compiled), "SN"),
            (new Regex(@"\s*Part\s*\d+\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Part N"),
            (new Regex(@"\s*\d+(?:st|nd|rd|th)\s+Season\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Nth Season"),
            (new Regex(@"\s*The\s+Final\s+Season\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase), "The Final Season"),

            // Roman numerals (only at the end, preceded by space)
            (new Regex(@"\s+(?:II|III|IV|V|VI)\s*$", RegexOptions.Compiled), "Roman II-VI"),
        };

        /// <summary>
        /// Removes season suffix from an anime title if present.
        /// </summary>
        /// <param name="title">The original anime title</param>
        /// <returns>A tuple containing the cleaned title and a boolean indicating if cleaning occurred</returns>
        public static (string CleanedTitle, bool WasCleaned) RemoveSeasonSuffix(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return (title, false);

            // Check if title is in exclusion list
            var trimmedTitle = title.Trim();
            if (ExcludedTitles.Contains(trimmedTitle))
                return (trimmedTitle, false);

            // Try each pattern
            foreach (var (pattern, _) in SeasonPatterns)
            {
                if (pattern.IsMatch(trimmedTitle))
                {
                    var cleaned = pattern.Replace(trimmedTitle, "").Trim();

                    // Don't return empty or very short titles
                    if (cleaned.Length >= 2)
                        return (cleaned, true);
                }
            }

            return (trimmedTitle, false);
        }

        /// <summary>
        /// Checks if a title contains a season suffix pattern.
        /// </summary>
        /// <param name="title">The title to check</param>
        /// <returns>True if the title contains a season suffix</returns>
        public static bool HasSeasonSuffix(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return false;

            var trimmedTitle = title.Trim();
            if (ExcludedTitles.Contains(trimmedTitle))
                return false;

            return SeasonPatterns.Any(p => p.Pattern.IsMatch(trimmedTitle));
        }
    }
}
