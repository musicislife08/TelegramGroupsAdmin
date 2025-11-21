namespace TelegramGroupsAdmin.Core.Utilities;

/// <summary>
/// TimeSpan parsing and formatting utilities shared across all domain libraries
/// </summary>
public static class TimeSpanUtilities
{
    /// <summary>
    /// Try to parse a duration string into a TimeSpan
    /// Supports formats: 5m, 1h, 24h, 7d, 1w, 1M, 1y, 5min, 1hr, 1hour, 24hours, 7days, 1week, 1month, 1year
    /// </summary>
    /// <param name="input">Duration string to parse</param>
    /// <param name="duration">Parsed TimeSpan value if successful</param>
    /// <returns>True if parsing succeeded, false otherwise</returns>
    public static bool TryParseDuration(string input, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;

        // Support formats: 5m, 1h, 24h, 7d, 1w, 1M, 1y, 5min, 1hr, 1hour, 24hours, 7days, 1week, 1month, 1year
        // Note: Keep original case for 'M' (month) vs 'm' (minute) detection
        var inputTrimmed = input.Trim();
        var inputLower = inputTrimmed.ToLower();

        // Check for month first (case-sensitive 'M' or word 'month')
        if (inputTrimmed.EndsWith("M") || inputLower.EndsWith("month") || inputLower.EndsWith("months"))
        {
            var numberPart = inputTrimmed.EndsWith("M")
                ? inputTrimmed.TrimEnd('M')
                : inputLower.TrimEnd('m', 'o', 'n', 't', 'h', 's');
            if (int.TryParse(numberPart, out var months))
            {
                // Approximate: 1 month = 30 days
                duration = TimeSpan.FromDays(months * 30);
                return true;
            }
        }
        // Check for year
        else if (inputLower.EndsWith("y") || inputLower.EndsWith("yr") || inputLower.EndsWith("year") || inputLower.EndsWith("years"))
        {
            var numberPart = inputLower.TrimEnd('y', 'r', 'e', 'a', 's');
            if (int.TryParse(numberPart, out var years))
            {
                // Approximate: 1 year = 365 days
                duration = TimeSpan.FromDays(years * 365);
                return true;
            }
        }
        // Minutes (lowercase 'm' or word 'min')
        else if (inputLower.EndsWith("m") || inputLower.EndsWith("min") || inputLower.EndsWith("mins"))
        {
            var numberPart = inputLower.TrimEnd('m', 'i', 'n', 's');
            if (int.TryParse(numberPart, out var minutes))
            {
                duration = TimeSpan.FromMinutes(minutes);
                return true;
            }
        }
        else if (inputLower.EndsWith("h") || inputLower.EndsWith("hr") || inputLower.EndsWith("hrs") || inputLower.EndsWith("hour") || inputLower.EndsWith("hours"))
        {
            var numberPart = inputLower.TrimEnd('h', 'r', 's', 'o', 'u');
            if (int.TryParse(numberPart, out var hours))
            {
                duration = TimeSpan.FromHours(hours);
                return true;
            }
        }
        else if (inputLower.EndsWith("d") || inputLower.EndsWith("day") || inputLower.EndsWith("days"))
        {
            var numberPart = inputLower.TrimEnd('d', 'a', 'y', 's');
            if (int.TryParse(numberPart, out var days))
            {
                duration = TimeSpan.FromDays(days);
                return true;
            }
        }
        else if (inputLower.EndsWith("w") || inputLower.EndsWith("week") || inputLower.EndsWith("weeks"))
        {
            var numberPart = inputLower.TrimEnd('w', 'e', 'k', 's');
            if (int.TryParse(numberPart, out var weeks))
            {
                duration = TimeSpan.FromDays(weeks * 7);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Format a TimeSpan duration as a human-readable string
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
