namespace TelegramGroupsAdmin.Core.Utilities;

/// <summary>
/// TimeSpan parsing and formatting utilities shared across all domain libraries
/// </summary>
public static class TimeSpanUtilities
{
    /// <summary>
    /// Try to parse a duration string into a TimeSpan
    /// Supports formats: 5m, 1h, 24h, 5min, 1hr, 1hour, 24hours
    /// </summary>
    /// <param name="input">Duration string to parse</param>
    /// <param name="duration">Parsed TimeSpan value if successful</param>
    /// <returns>True if parsing succeeded, false otherwise</returns>
    public static bool TryParseDuration(string input, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;

        // Support formats: 5m, 1h, 24h, 5min, 1hr, 1hour, 24hours
        input = input.ToLower().Trim();

        if (input.EndsWith("m") || input.EndsWith("min") || input.EndsWith("mins"))
        {
            var numberPart = input.TrimEnd('m', 'i', 'n', 's');
            if (int.TryParse(numberPart, out var minutes))
            {
                duration = TimeSpan.FromMinutes(minutes);
                return true;
            }
        }
        else if (input.EndsWith("h") || input.EndsWith("hr") || input.EndsWith("hrs") || input.EndsWith("hour") || input.EndsWith("hours"))
        {
            var numberPart = input.TrimEnd('h', 'r', 's', 'o', 'u');
            if (int.TryParse(numberPart, out var hours))
            {
                duration = TimeSpan.FromHours(hours);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Format a TimeSpan duration as a human-readable string
    /// </summary>
    /// <param name="duration">TimeSpan to format</param>
    /// <returns>Formatted string like "5 minutes", "1 hour", "2 days"</returns>
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
        else
        {
            return $"{(int)duration.TotalDays} day{((int)duration.TotalDays != 1 ? "s" : "")}";
        }
    }
}
