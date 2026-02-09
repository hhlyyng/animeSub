using System.Text.RegularExpressions;

namespace backend.Services.Utils;

/// <summary>
/// Utility for extracting and normalizing torrent info hashes.
/// Supports hex BTIH (40 chars) and base32 BTIH (32 chars).
/// </summary>
public static class TorrentHashHelper
{
    private static readonly Regex HexHashRegex = new(
        @"(?<![A-Fa-f0-9])([A-Fa-f0-9]{40})(?![A-Fa-f0-9])",
        RegexOptions.Compiled);

    private static readonly Regex BtihRegex = new(
        @"btih:([A-Za-z0-9]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string? ResolveHash(params string?[] sources)
    {
        foreach (var source in sources)
        {
            var extracted = ExtractHash(source);
            if (!string.IsNullOrWhiteSpace(extracted))
            {
                return extracted;
            }
        }

        return null;
    }

    public static string? NormalizeHash(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = raw.Trim();
        if (value.Length == 40 && IsHex(value))
        {
            return value.ToUpperInvariant();
        }

        if (value.Length == 32 && TryDecodeBase32(value, out var bytes) && bytes.Length == 20)
        {
            return Convert.ToHexString(bytes);
        }

        return null;
    }

    private static string? ExtractHash(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        foreach (var candidate in GetCandidates(source))
        {
            var normalizedDirect = NormalizeHash(candidate);
            if (normalizedDirect != null)
            {
                return normalizedDirect;
            }

            var btihMatch = BtihRegex.Match(candidate);
            if (btihMatch.Success && btihMatch.Groups.Count > 1)
            {
                var normalizedBtih = NormalizeHash(btihMatch.Groups[1].Value);
                if (normalizedBtih != null)
                {
                    return normalizedBtih;
                }
            }

            var hexMatch = HexHashRegex.Match(candidate);
            if (hexMatch.Success && hexMatch.Groups.Count > 1)
            {
                return hexMatch.Groups[1].Value.ToUpperInvariant();
            }
        }

        return null;
    }

    private static IEnumerable<string> GetCandidates(string source)
    {
        yield return source;

        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(source);
        }
        catch
        {
            yield break;
        }

        if (!decoded.Equals(source, StringComparison.Ordinal))
        {
            yield return decoded;
        }
    }

    private static bool IsHex(string value)
    {
        foreach (var ch in value)
        {
            if (!Uri.IsHexDigit(ch))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryDecodeBase32(string value, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().ToUpperInvariant();
        if (!Regex.IsMatch(normalized, "^[A-Z2-7]{32}$"))
        {
            return false;
        }

        var output = new List<byte>(20);
        var bitBuffer = 0;
        var bitCount = 0;

        foreach (var c in normalized)
        {
            var index = Base32Alphabet.IndexOf(c);
            if (index < 0)
            {
                return false;
            }

            bitBuffer = (bitBuffer << 5) | index;
            bitCount += 5;

            while (bitCount >= 8)
            {
                bitCount -= 8;
                var valueByte = (byte)((bitBuffer >> bitCount) & 0xFF);
                output.Add(valueByte);
            }
        }

        if (output.Count != 20)
        {
            return false;
        }

        bytes = output.ToArray();
        return true;
    }
}
