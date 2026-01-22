using System.Collections.Frozen;
using System.Text;

namespace TelegramGroupsAdmin.Core.Utilities;

/// <summary>
/// Utility methods for formatting text for Telegram messages.
/// </summary>
public static class TelegramTextUtilities
{
    /// <summary>
    /// Characters that must be escaped in Telegram MarkdownV2 format.
    /// FrozenSet provides optimized read-only lookups for this static character set.
    /// </summary>
    private static readonly FrozenSet<char> MarkdownV2SpecialChars =
        FrozenSet.ToFrozenSet(['_', '*', '[', ']', '(', ')', '~', '`', '>', '#', '+', '-', '=', '|', '{', '}', '.', '!']);

    /// <summary>
    /// Escapes special characters for Telegram MarkdownV2 format.
    /// MarkdownV2 requires these characters to be escaped with a preceding backslash.
    /// Single-pass O(n) implementation using StringBuilder.
    /// </summary>
    /// <param name="text">The text to escape</param>
    /// <returns>Text with special characters escaped for MarkdownV2</returns>
    public static string EscapeMarkdownV2(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var escapedText = new StringBuilder(text.Length * 2);
        foreach (var character in text)
        {
            if (MarkdownV2SpecialChars.Contains(character))
                escapedText.Append('\\');
            escapedText.Append(character);
        }

        return escapedText.ToString();
    }
}
