namespace TelegramGroupsAdmin.Core.Utilities;

/// <summary>
/// DateTime/DateTimeOffset formatting utilities shared across all UI components.
/// Provides relative time formatting in both compact and verbose styles.
/// </summary>
public static class DateTimeUtilities
{
    /// <summary>
    /// Format a timestamp as a compact relative time string.
    /// Suitable for dense UIs where space is limited.
    /// </summary>
    /// <param name="timestamp">The timestamp to format</param>
    /// <returns>Compact string like "5m ago", "3h ago", "2d ago", or "MMM d" for dates older than 7 days</returns>
    public static string FormatRelativeTimeCompact(DateTimeOffset timestamp)
    {
        var diff = DateTimeOffset.UtcNow - timestamp;

        return diff switch
        {
            { TotalMinutes: < 1 } => "just now",
            { TotalMinutes: < 60 } => $"{(int)diff.TotalMinutes}m ago",
            { TotalHours: < 24 } => $"{(int)diff.TotalHours}h ago",
            { TotalDays: < 7 } => $"{(int)diff.TotalDays}d ago",
            _ => timestamp.ToString("MMM d")
        };
    }

    /// <summary>
    /// Format a timestamp as a verbose relative time string.
    /// Suitable for UIs where readability is prioritized.
    /// Reuses TimeSpanUtilities.FormatDuration for consistent pluralization.
    /// </summary>
    /// <param name="timestamp">The timestamp to format</param>
    /// <returns>Verbose string like "5 minutes ago", "3 hours ago", or "MMM d, yyyy" for dates older than 30 days</returns>
    public static string FormatRelativeTimeVerbose(DateTimeOffset timestamp)
    {
        var span = DateTimeOffset.UtcNow - timestamp;

        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalDays >= 30) return timestamp.ToString("MMM d, yyyy");

        return TimeSpanUtilities.FormatDuration(span) + " ago";
    }
}
