namespace TelegramGroupsAdmin.Models.Analytics;

/// <summary>
/// Spam trend comparisons across multiple time periods for the Analytics page.
/// Provides week-over-week, month-over-month, and year-over-year insights.
/// </summary>
public class SpamTrendComparison
{
    #region Week-over-Week

    /// <summary>
    /// Spam count this week (current week in user's timezone, including today)
    /// </summary>
    public int ThisWeekSpamCount { get; set; }

    /// <summary>
    /// Spam count last week (previous 7-day period). Null if insufficient data.
    /// </summary>
    public int? LastWeekSpamCount { get; set; }

    /// <summary>
    /// Percentage change week-over-week. Positive = more spam this week.
    /// Null if no last week data.
    /// </summary>
    public double? WeekOverWeekChange { get; set; }

    #endregion

    #region Month-over-Month

    /// <summary>
    /// Spam count this month (current calendar month in user's timezone)
    /// </summary>
    public int ThisMonthSpamCount { get; set; }

    /// <summary>
    /// Spam count last month (previous calendar month). Null if insufficient data.
    /// </summary>
    public int? LastMonthSpamCount { get; set; }

    /// <summary>
    /// Percentage change month-over-month. Positive = more spam this month.
    /// Null if no last month data.
    /// </summary>
    public double? MonthOverMonthChange { get; set; }

    #endregion

    #region Year-over-Year

    /// <summary>
    /// Spam count this year (current calendar year in user's timezone)
    /// </summary>
    public int ThisYearSpamCount { get; set; }

    /// <summary>
    /// Spam count last year (previous calendar year). Null if insufficient data.
    /// </summary>
    public int? LastYearSpamCount { get; set; }

    /// <summary>
    /// Percentage change year-over-year. Positive = more spam this year.
    /// Null if no last year data.
    /// </summary>
    public double? YearOverYearChange { get; set; }

    #endregion

    #region Computed Properties

    /// <summary>
    /// True if we have last week's data for comparison
    /// </summary>
    public bool HasLastWeekData => LastWeekSpamCount.HasValue;

    /// <summary>
    /// True if we have last month's data for comparison
    /// </summary>
    public bool HasLastMonthData => LastMonthSpamCount.HasValue;

    /// <summary>
    /// True if we have last year's data for comparison
    /// </summary>
    public bool HasLastYearData => LastYearSpamCount.HasValue;

    /// <summary>
    /// True if week-over-week spam is improving (less spam)
    /// </summary>
    public bool IsWeekImproving => WeekOverWeekChange.HasValue && WeekOverWeekChange.Value < 0;

    /// <summary>
    /// True if month-over-month spam is improving (less spam)
    /// </summary>
    public bool IsMonthImproving => MonthOverMonthChange.HasValue && MonthOverMonthChange.Value < 0;

    /// <summary>
    /// True if year-over-year spam is improving (less spam)
    /// </summary>
    public bool IsYearImproving => YearOverYearChange.HasValue && YearOverYearChange.Value < 0;

    #endregion
}
