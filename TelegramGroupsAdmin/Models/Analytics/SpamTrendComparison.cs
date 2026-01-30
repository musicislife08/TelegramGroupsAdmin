namespace TelegramGroupsAdmin.Models.Analytics;

/// <summary>
/// Spam trend comparisons across multiple time periods for the Analytics page.
/// Provides week-over-week, month-over-month, and year-over-year insights.
/// Year-over-year compares same period (e.g., Jan 1-25 this year vs Jan 1-25 last year).
/// </summary>
public record SpamTrendComparison
{
    private const double AverageDaysPerMonth = 30.44;

    #region Week-over-Week

    /// <summary>
    /// Spam count this week (current week in user's timezone, including today)
    /// </summary>
    public int ThisWeekSpamCount { get; init; }

    /// <summary>
    /// Spam count last week (previous 7-day period). 0 if no data.
    /// </summary>
    public int LastWeekSpamCount { get; init; }

    /// <summary>
    /// Number of days in the current week period (1-7)
    /// </summary>
    public int DaysInThisWeek { get; init; }

    /// <summary>
    /// Number of days in the last week period (always 7)
    /// </summary>
    public int DaysInLastWeek { get; init; }

    /// <summary>
    /// Percentage change week-over-week. Positive = more spam this week.
    /// Null if last week count is 0 (can't divide by zero).
    /// </summary>
    public double? WeekOverWeekChange { get; init; }

    #endregion

    #region Month-over-Month

    /// <summary>
    /// Spam count this month (current calendar month in user's timezone)
    /// </summary>
    public int ThisMonthSpamCount { get; init; }

    /// <summary>
    /// Spam count last month (previous calendar month). 0 if no data.
    /// </summary>
    public int LastMonthSpamCount { get; init; }

    /// <summary>
    /// Number of days in the current month period (1-31)
    /// </summary>
    public int DaysInThisMonth { get; init; }

    /// <summary>
    /// Number of days in the last month (28-31)
    /// </summary>
    public int DaysInLastMonth { get; init; }

    /// <summary>
    /// Percentage change month-over-month. Positive = more spam this month.
    /// Null if last month count is 0 (can't divide by zero).
    /// </summary>
    public double? MonthOverMonthChange { get; init; }

    #endregion

    #region Year-over-Year

    /// <summary>
    /// Spam count this year-to-date (Jan 1 to today in user's timezone)
    /// </summary>
    public int ThisYearSpamCount { get; init; }

    /// <summary>
    /// Spam count for same period last year (Jan 1 to same day last year). 0 if no data.
    /// </summary>
    public int LastYearSpamCount { get; init; }

    /// <summary>
    /// Number of days in current year-to-date period
    /// </summary>
    public int DaysInThisYear { get; init; }

    /// <summary>
    /// Number of days in the same period last year
    /// </summary>
    public int DaysInLastYear { get; init; }

    /// <summary>
    /// Percentage change year-over-year. Positive = more spam this year.
    /// Null if last year count is 0 (can't divide by zero).
    /// </summary>
    public double? YearOverYearChange { get; init; }

    #endregion

    #region Computed Properties - Differences

    /// <summary>
    /// Absolute difference in spam count week-over-week
    /// </summary>
    public int WeekDifference => ThisWeekSpamCount - LastWeekSpamCount;

    /// <summary>
    /// Absolute difference in spam count month-over-month
    /// </summary>
    public int MonthDifference => ThisMonthSpamCount - LastMonthSpamCount;

    /// <summary>
    /// Absolute difference in spam count year-over-year
    /// </summary>
    public int YearDifference => ThisYearSpamCount - LastYearSpamCount;

    #endregion

    #region Computed Properties - Can Show Percentage

    /// <summary>
    /// True if percentage can be calculated (last week count > 0)
    /// </summary>
    public bool CanShowWeekPercent => LastWeekSpamCount > 0;

    /// <summary>
    /// True if percentage can be calculated (last month count > 0)
    /// </summary>
    public bool CanShowMonthPercent => LastMonthSpamCount > 0;

    /// <summary>
    /// True if percentage can be calculated (last year count > 0)
    /// </summary>
    public bool CanShowYearPercent => LastYearSpamCount > 0;

    #endregion

    #region Computed Properties - Averages

    /// <summary>
    /// Average spam per day this week
    /// </summary>
    public double ThisWeekPerDay => DaysInThisWeek > 0 ? (double)ThisWeekSpamCount / DaysInThisWeek : 0;

    /// <summary>
    /// Average spam per day last week
    /// </summary>
    public double LastWeekPerDay => DaysInLastWeek > 0 ? (double)LastWeekSpamCount / DaysInLastWeek : 0;

    /// <summary>
    /// Average spam per week this month
    /// </summary>
    public double ThisMonthPerWeek => DaysInThisMonth > 0 ? ThisMonthSpamCount / (DaysInThisMonth / 7.0) : 0;

    /// <summary>
    /// Average spam per week last month
    /// </summary>
    public double LastMonthPerWeek => DaysInLastMonth > 0 ? LastMonthSpamCount / (DaysInLastMonth / 7.0) : 0;

    /// <summary>
    /// Average spam per month this year (using 30.44 days per month average)
    /// </summary>
    public double ThisYearPerMonth => DaysInThisYear > 0 ? ThisYearSpamCount / (DaysInThisYear / AverageDaysPerMonth) : 0;

    /// <summary>
    /// Average spam per month for same period last year
    /// </summary>
    public double LastYearPerMonth => DaysInLastYear > 0 ? LastYearSpamCount / (DaysInLastYear / AverageDaysPerMonth) : 0;

    #endregion

    #region Computed Properties - Trend Direction

    /// <summary>
    /// True if week-over-week spam is improving (less spam)
    /// </summary>
    public bool IsWeekImproving => WeekDifference < 0;

    /// <summary>
    /// True if month-over-month spam is improving (less spam)
    /// </summary>
    public bool IsMonthImproving => MonthDifference < 0;

    /// <summary>
    /// True if year-over-year spam is improving (less spam)
    /// </summary>
    public bool IsYearImproving => YearDifference < 0;

    /// <summary>
    /// True if week-over-week spam is worsening (more spam)
    /// </summary>
    public bool IsWeekWorsening => WeekDifference > 0;

    /// <summary>
    /// True if month-over-month spam is worsening (more spam)
    /// </summary>
    public bool IsMonthWorsening => MonthDifference > 0;

    /// <summary>
    /// True if year-over-year spam is worsening (more spam)
    /// </summary>
    public bool IsYearWorsening => YearDifference > 0;

    #endregion
}
