namespace TelegramGroupsAdmin.Telegram.Constants;

/// <summary>
/// Centralized constants for analytics calculations and data aggregation.
/// </summary>
public static class AnalyticsConstants
{
    /// <summary>
    /// Default limit for recent detections query (100 records)
    /// </summary>
    public const int DefaultRecentDetectionsLimit = 100;

    /// <summary>
    /// Lookback period for "Last 24 hours" statistics (1 day)
    /// </summary>
    public const int Last24HoursLookbackDays = -1;

    /// <summary>
    /// Top N active users to display in analytics (5 users)
    /// </summary>
    public const int TopActiveUsersLimit = 5;

    /// <summary>
    /// Top N hours to include in peak activity detection (5 hours)
    /// </summary>
    public const int TopPeakHoursCount = 5;

    /// <summary>
    /// Top N days to include in peak activity detection (3 days)
    /// </summary>
    public const int TopPeakDaysCount = 3;

    /// <summary>
    /// Top N months to include in peak activity detection (3 months)
    /// </summary>
    public const int TopPeakMonthsCount = 3;

    /// <summary>
    /// Minimum days of data required for weekly pattern analysis (7 days)
    /// </summary>
    public const int MinDaysForWeeklyPattern = 7;

    /// <summary>
    /// Minimum days of data required for week-over-week growth comparison (14 days)
    /// </summary>
    public const int MinDaysForWeekOverWeekGrowth = 14;

    /// <summary>
    /// Minimum days of data required for monthly pattern analysis (60 days)
    /// </summary>
    public const int MinDaysForMonthlyPattern = 60;

    /// <summary>
    /// Lookback period for current week in week-over-week growth (7 days)
    /// </summary>
    public const int CurrentWeekLookbackDays = -7;

    /// <summary>
    /// Lookback period for previous week in week-over-week growth (14 days)
    /// </summary>
    public const int PreviousWeekLookbackDays = -14;

    /// <summary>
    /// Hour for midnight in 24-hour format
    /// </summary>
    public const int MidnightHour = 0;

    /// <summary>
    /// Hour for noon in 24-hour format
    /// </summary>
    public const int NoonHour = 12;

    /// <summary>
    /// Offset for converting 24-hour to 12-hour format (PM hours)
    /// </summary>
    public const int TwelveHourOffset = 12;

    /// <summary>
    /// Character count for abbreviated day names (first 3 characters)
    /// </summary>
    public const int DayNameAbbreviationLength = 3;

    /// <summary>
    /// Minimum consecutive days required for day range formatting (2 days)
    /// </summary>
    public const int MinConsecutiveDaysForRange = 1;

    /// <summary>
    /// Dummy year for month name formatting (2000)
    /// </summary>
    public const int MonthNameFormattingYear = 2000;

    /// <summary>
    /// Day of month for month name formatting (1st day)
    /// </summary>
    public const int MonthNameFormattingDay = 1;
}
