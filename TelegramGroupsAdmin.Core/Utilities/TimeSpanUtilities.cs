namespace TelegramGroupsAdmin.Core.Utilities;

/// <summary>
/// TimeSpan parsing and formatting utilities shared across all domain libraries
/// </summary>
public static class TimeSpanUtilities
{
    /// <summary>
    /// Try to parse a duration string into a TimeSpan.
    /// Supports short formats: 5m (minutes), 1h (hours), 7d (days), 2w (weeks), 1M (months), 1y (years)
    /// Note: 'M' (uppercase) = months, 'm' (lowercase) = minutes
    /// </summary>
    /// <param name="input">Duration string to parse (e.g., "30m", "24h", "7d", "2w", "1M", "1y")</param>
    /// <param name="duration">Parsed TimeSpan value if successful</param>
    /// <returns>True if parsing succeeded, false otherwise</returns>
    public static bool TryParseDuration(string input, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        var trimmed = input.Trim();
        if (trimmed.Length < 2)
            return false;

        // Extract unit (last character) and number part
        var unit = trimmed[^1];
        var numberPart = trimmed[..^1];

        // Reject if number part contains spaces (e.g., "5 m")
        if (numberPart.Contains(' '))
            return false;

        if (!int.TryParse(numberPart, out var value) || value < 0)
            return false;

        // Note: 'M' (uppercase) = months, 'm' (lowercase) = minutes
        switch (unit)
        {
            case 'm': // minutes (lowercase)
                duration = TimeSpan.FromMinutes(value);
                return true;

            case 'h':
            case 'H':
                duration = TimeSpan.FromHours(value);
                return true;

            case 'd':
            case 'D':
                duration = TimeSpan.FromDays(value);
                return true;

            case 'w':
            case 'W':
                duration = TimeSpan.FromDays(value * 7);
                return true;

            case 'M': // months (uppercase only)
                duration = TimeSpan.FromDays(value * 30);
                return true;

            case 'y':
            case 'Y':
                duration = TimeSpan.FromDays(value * 365);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Format a TimeSpan duration as a human-readable string.
    /// Consider using Humanizer's TimeSpan.Humanize() for more accurate formatting.
    /// </summary>
    /// <param name="duration">TimeSpan to format</param>
    /// <returns>Formatted string like "5 minutes", "1 hour", "2 days", "1 week", "1 month", "1 year"</returns>
    public static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes < 60)
        {
            return $"{(int)duration.TotalMinutes} minute{((int)duration.TotalMinutes != 1 ? "s" : "")}";
        }
        else if (duration.TotalHours < 24)
        {
            return $"{(int)duration.TotalHours} hour{((int)duration.TotalHours != 1 ? "s" : "")}";
        }
        else if (duration.TotalDays < 7)
        {
            return $"{(int)duration.TotalDays} day{((int)duration.TotalDays != 1 ? "s" : "")}";
        }
        else if (duration.TotalDays < 30)
        {
            var weeks = (int)(duration.TotalDays / 7);
            return $"{weeks} week{(weeks != 1 ? "s" : "")}";
        }
        else if (duration.TotalDays < 365)
        {
            var months = (int)(duration.TotalDays / 30);
            return $"{months} month{(months != 1 ? "s" : "")}";
        }
        else
        {
            var years = (int)(duration.TotalDays / 365);
            return $"{years} year{(years != 1 ? "s" : "")}";
        }
    }
}
