using System.Text.RegularExpressions;

namespace TelegramGroupsAdmin.Core.Utilities;

/// <summary>
/// URL extraction and manipulation utilities shared across all domain libraries
/// </summary>
public static partial class UrlUtilities
{
    /// <summary>
    /// Extract URLs from text using regex pattern matching
    /// Matches http:// and https:// URLs
    /// </summary>
    /// <param name="text">Text to extract URLs from</param>
    /// <returns>List of extracted URLs, or null if no URLs found</returns>
    public static List<string>? ExtractUrls(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var matches = UrlRegex().Matches(text);
        return matches.Count > 0
            ? matches.Select(m => m.Value).ToList()
            : null;
    }

    /// <summary>
    /// Compiled regex for URL matching
    /// Pattern: https?://[^\s\]\)\>]+
    /// Matches http/https URLs until whitespace or closing brackets
    /// </summary>
    [GeneratedRegex(@"https?://[^\s\]\)\>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex UrlRegex();
}
