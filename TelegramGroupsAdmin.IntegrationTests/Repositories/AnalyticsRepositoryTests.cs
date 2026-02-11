using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Repositories;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

/// <summary>
/// Integration tests for AnalyticsRepository.
///
/// Tests cover all IAnalyticsRepository methods added during Analytics Enhancement Phase:
/// - GetDailySpamSummaryAsync: Dashboard spam today vs yesterday comparison
/// - GetSpamTrendComparisonAsync: Week/month/year spam trend comparisons
/// - GetDetectionAccuracyStatsAsync: FP/FN accuracy metrics
/// - GetAlgorithmPerformanceStatsAsync: Per-algorithm timing metrics
/// - GetDetectionMethodComparisonAsync: Algorithm effectiveness comparison
/// - Welcome system analytics: Join trends, response distribution, per-chat stats
///
/// Test Data:
/// - Base data from SQL scripts 00-06 (users, chats, messages, ham detections)
/// - Analytics-specific data from 50_analytics_test_data.sql (spam detections, corrections, welcome responses)
///
/// Test Infrastructure:
/// - Shared PostgreSQL container (PostgresFixture) - started once per test run
/// - Unique database per test (test_db_xxx) - perfect isolation
/// - GoldenDataset.SeedAnalyticsDataAsync for analytics-specific data
/// </summary>
[TestFixture]
public class AnalyticsRepositoryTests
{
    private MigrationTestHelper _testHelper = null!;
    private IServiceProvider _serviceProvider = null!;
    private IServiceScope _scope = null!;
    private IAnalyticsRepository _analyticsRepository = null!;

    private const string DefaultTimeZoneId = "UTC";

    [SetUp]
    public async Task SetUp()
    {
        // Create unique test database with migrations applied
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

        // Set up dependency injection
        var services = new ServiceCollection();

        // Add NpgsqlDataSource
        var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(_testHelper.ConnectionString);
        services.AddSingleton(dataSourceBuilder.Build());

        // Add DbContextFactory
        services.AddDbContextFactory<AppDbContext>((_, options) =>
        {
            options.UseNpgsql(_testHelper.ConnectionString);
        });

        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning);
        });

        // Register AnalyticsRepository
        services.AddScoped<IAnalyticsRepository, AnalyticsRepository>();

        _serviceProvider = services.BuildServiceProvider();

        // Seed base data + analytics test data
        var contextFactory = _serviceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var context = await contextFactory.CreateDbContextAsync())
        {
            // Seed base data (users, chats, messages, configs) without training labels
            await GoldenDataset.SeedWithoutTrainingDataAsync(context);
            // Seed analytics-specific data (spam detections, corrections, welcome responses)
            await GoldenDataset.SeedAnalyticsDataAsync(context);
        }

        // Create repository instance with scoped lifetime
        _scope = _serviceProvider.CreateScope();
        _analyticsRepository = _scope.ServiceProvider.GetRequiredService<IAnalyticsRepository>();
    }

    [TearDown]
    public void TearDown()
    {
        _scope?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
        _testHelper?.Dispose();
    }

    #region GetDailySpamSummaryAsync Tests

    [Test]
    public async Task GetDailySpamSummary_WithTodayAndYesterday_ReturnsComparison()
    {
        // Act
        var summary = await _analyticsRepository.GetDailySpamSummaryAsync(DefaultTimeZoneId);

        // Assert
        Assert.That(summary, Is.Not.Null);
        // Today's spam includes: base data (82581) + analytics automated spam + manual corrections
        Assert.That(summary.TodaySpamCount, Is.GreaterThanOrEqualTo(GoldenDataset.AnalyticsData.TodaySpamCount),
            "Should have at least the analytics spam detections today");
        Assert.That(summary.HasYesterdayData, Is.True, "Should have yesterday data");
        Assert.That(summary.YesterdaySpamCount, Is.GreaterThanOrEqualTo(GoldenDataset.AnalyticsData.YesterdaySpamCount),
            "Should have at least the analytics spam detections yesterday");
    }

    [Test]
    public async Task GetDailySpamSummary_WithMoreSpamToday_IsWorseningTrue()
    {
        // Act
        var summary = await _analyticsRepository.GetDailySpamSummaryAsync(DefaultTimeZoneId);

        // Assert - Today has 3 spam, yesterday has 2, so it's worsening
        Assert.That(summary.HasYesterdayData, Is.True);
        Assert.That(summary.TodaySpamCount, Is.GreaterThan(summary.YesterdaySpamCount!.Value));
        Assert.That(summary.IsWorsening, Is.True, "Should be worsening when today > yesterday");
        Assert.That(summary.IsImproving, Is.False, "Should not be improving when today > yesterday");
    }

    [Test]
    public async Task GetDailySpamSummary_SpamCountChange_CalculatedCorrectly()
    {
        // Act
        var summary = await _analyticsRepository.GetDailySpamSummaryAsync(DefaultTimeZoneId);

        // Assert
        Assert.That(summary.HasYesterdayData, Is.True);
        Assert.That(summary.SpamCountChange, Is.Not.Null);
        Assert.That(summary.SpamCountChange!.Value,
            Is.EqualTo(summary.TodaySpamCount - summary.YesterdaySpamCount!.Value),
            "SpamCountChange should equal TodaySpamCount - YesterdaySpamCount");
    }

    [Test]
    public async Task GetDailySpamSummary_TodayTotalDetections_IncludesHamAndSpam()
    {
        // Act
        var summary = await _analyticsRepository.GetDailySpamSummaryAsync(DefaultTimeZoneId);

        // Assert
        Assert.That(summary.TodayTotalDetections, Is.GreaterThanOrEqualTo(summary.TodaySpamCount),
            "Total detections should be >= spam count");
        Assert.That(summary.TodayTotalDetections, Is.EqualTo(summary.TodaySpamCount + summary.TodayHamCount),
            "Total detections should equal spam + ham");
    }

    [Test]
    public async Task GetDailySpamSummary_SpamRate_CalculatedCorrectly()
    {
        // Act
        var summary = await _analyticsRepository.GetDailySpamSummaryAsync(DefaultTimeZoneId);

        // Assert - verify we have data to test against
        Assert.That(summary.TodayTotalDetections, Is.GreaterThan(0),
            "Test data should provide detections for rate calculation");

        // Spam rate should be between 0-100 and proportional to spam count
        Assert.That(summary.TodaySpamRate, Is.GreaterThanOrEqualTo(0).And.LessThanOrEqualTo(100),
            "Spam rate should be a valid percentage");
        Assert.That(summary.TodaySpamRate, Is.GreaterThan(0),
            "With spam in test data, rate should be positive");
    }

    [Test]
    public async Task GetDailySpamSummary_DifferentTimezone_GroupsCorrectly()
    {
        // Test with different timezone to ensure timezone handling works
        const string pacificTimeZone = "America/Los_Angeles";

        // Act
        var summary = await _analyticsRepository.GetDailySpamSummaryAsync(pacificTimeZone);

        // Assert
        Assert.That(summary, Is.Not.Null);
        // Should still have data, just potentially grouped differently
        Assert.That(summary.TodayTotalDetections, Is.GreaterThanOrEqualTo(0));
    }

    #endregion

    #region GetSpamTrendComparisonAsync Tests

    [Test]
    public async Task GetSpamTrendComparison_ThisWeekSpam_CountsCorrectly()
    {
        // Act
        var trends = await _analyticsRepository.GetSpamTrendComparisonAsync(DefaultTimeZoneId);

        // Assert
        Assert.That(trends, Is.Not.Null);
        // This week should include today's 3 spam + yesterday's 2 spam = 5 minimum
        // (depending on timezone, could be more from last week's data)
        Assert.That(trends.ThisWeekSpamCount, Is.GreaterThanOrEqualTo(
            GoldenDataset.AnalyticsData.TodaySpamCount + GoldenDataset.AnalyticsData.YesterdaySpamCount),
            "This week should include at least today + yesterday spam");
    }

    [Test]
    public async Task GetSpamTrendComparison_LastWeekSpamCount_WhenDataExists()
    {
        // Analytics test data includes spam from 8-9 days ago
        // Act
        var trends = await _analyticsRepository.GetSpamTrendComparisonAsync(DefaultTimeZoneId);

        // Assert
        Assert.That(trends.LastWeekSpamCount, Is.EqualTo(GoldenDataset.AnalyticsData.LastWeekSpamCount),
            "Last week should have 2 spam detections from analytics test data");
        Assert.That(trends.CanShowWeekPercent, Is.True,
            "Should be able to show percentage since last week has data");
    }

    [Test]
    public async Task GetSpamTrendComparison_WeekOverWeekChange_CalculatedCorrectly()
    {
        // Act
        var trends = await _analyticsRepository.GetSpamTrendComparisonAsync(DefaultTimeZoneId);

        // Assert - test data guarantees last week data exists
        Assert.That(trends.CanShowWeekPercent, Is.True,
            "Test data should provide last week spam for comparison");
        Assert.That(trends.LastWeekSpamCount, Is.EqualTo(GoldenDataset.AnalyticsData.LastWeekSpamCount),
            "Last week should have exactly 2 spam from test data");

        // With 2 spam last week and 5+ this week, change should be positive (more spam)
        Assert.That(trends.WeekOverWeekChange, Is.Not.Null);
        Assert.That(trends.WeekOverWeekChange!.Value, Is.GreaterThan(0),
            "Week-over-week change should be positive (more spam this week)");
    }

    [Test]
    public async Task GetSpamTrendComparison_IsWeekImproving_WhenLessSpam()
    {
        // Act
        var trends = await _analyticsRepository.GetSpamTrendComparisonAsync(DefaultTimeZoneId);

        // Assert - test data has more spam this week (5+) than last week (2)
        Assert.That(trends.CanShowWeekPercent, Is.True, "Test data should provide last week data");
        Assert.That(trends.WeekOverWeekChange, Is.Not.Null);

        // With more spam this week, IsWeekImproving should be false (worsening)
        Assert.That(trends.ThisWeekSpamCount, Is.GreaterThan(trends.LastWeekSpamCount),
            "Test data should have more spam this week than last week");
        Assert.That(trends.IsWeekImproving, Is.False,
            "Should not be improving when spam increased week-over-week");
        Assert.That(trends.IsWeekWorsening, Is.True,
            "Should be worsening when spam increased week-over-week");
    }

    [Test]
    public async Task GetSpamTrendComparison_ThisMonthSpam_IncludesTodayData()
    {
        // Act
        var trends = await _analyticsRepository.GetSpamTrendComparisonAsync(DefaultTimeZoneId);

        // Assert
        Assert.That(trends.ThisMonthSpamCount, Is.GreaterThanOrEqualTo(GoldenDataset.AnalyticsData.TodaySpamCount),
            "This month should include at least today's spam");
    }

    [Test]
    public async Task GetSpamTrendComparison_ThisYearSpam_IncludesAllRecentData()
    {
        // Act
        var trends = await _analyticsRepository.GetSpamTrendComparisonAsync(DefaultTimeZoneId);

        // Assert
        Assert.That(trends.ThisYearSpamCount, Is.GreaterThanOrEqualTo(
            GoldenDataset.AnalyticsData.TodaySpamCount +
            GoldenDataset.AnalyticsData.YesterdaySpamCount +
            GoldenDataset.AnalyticsData.LastWeekSpamCount),
            "This year should include all test data spam");
    }

    [Test]
    public async Task GetSpamTrendComparison_ZeroLastPeriod_PercentChangeNull()
    {
        // Test data only spans ~9 days, so last year's same period should have no data
        // Act
        var trends = await _analyticsRepository.GetSpamTrendComparisonAsync(DefaultTimeZoneId);

        // Assert - test data doesn't include last year data (0 instead of null now)
        Assert.That(trends.LastYearSpamCount, Is.EqualTo(0),
            "LastYearSpamCount should be 0 when no data exists (not null)");
        Assert.That(trends.CanShowYearPercent, Is.False,
            "CanShowYearPercent should be false when last year count is 0");
        Assert.That(trends.YearOverYearChange, Is.Null,
            "YearOverYearChange should be null when can't divide by zero");

        // But difference should still be calculable
        Assert.That(trends.YearDifference, Is.EqualTo(trends.ThisYearSpamCount),
            "YearDifference should equal this year count when last year is 0");
    }

    [Test]
    public async Task GetSpamTrendComparison_DaysInPeriod_PopulatedCorrectly()
    {
        // Act
        var trends = await _analyticsRepository.GetSpamTrendComparisonAsync(DefaultTimeZoneId);

        // Assert - DaysInX properties should be populated
        Assert.That(trends.DaysInThisWeek, Is.GreaterThan(0).And.LessThanOrEqualTo(7),
            "DaysInThisWeek should be between 1 and 7");
        Assert.That(trends.DaysInLastWeek, Is.EqualTo(7),
            "DaysInLastWeek should always be 7");
        Assert.That(trends.DaysInThisMonth, Is.GreaterThan(0).And.LessThanOrEqualTo(31),
            "DaysInThisMonth should be between 1 and 31");
        Assert.That(trends.DaysInLastMonth, Is.GreaterThan(27).And.LessThanOrEqualTo(31),
            "DaysInLastMonth should be between 28 and 31");
        Assert.That(trends.DaysInThisYear, Is.GreaterThan(0).And.LessThanOrEqualTo(366),
            "DaysInThisYear should be between 1 and 366");
        Assert.That(trends.DaysInLastYear, Is.GreaterThan(0).And.LessThanOrEqualTo(366),
            "DaysInLastYear should be positive (same period comparison)");
    }

    [Test]
    public async Task GetSpamTrendComparison_YearOverYear_ComparesSamePeriod()
    {
        // Year-over-year should compare Jan 1-today vs Jan 1-today last year
        // On Feb 29 of a leap year, AddYears(-1) clamps to Feb 28 → 1 day difference
        // Act
        var trends = await _analyticsRepository.GetSpamTrendComparisonAsync(DefaultTimeZoneId);

        // Assert - DaysInThisYear and DaysInLastYear should be equal (or differ by 1 on leap day)
        Assert.That(trends.DaysInLastYear,
            Is.InRange(trends.DaysInThisYear - 1, trends.DaysInThisYear),
            "Year over Year should compare same periods (equal days, or 1 day less on leap day)");
    }

    [Test]
    public async Task GetSpamTrendComparison_Averages_ComputedCorrectly()
    {
        // Act
        var trends = await _analyticsRepository.GetSpamTrendComparisonAsync(DefaultTimeZoneId);

        // Assert - averages should be computed based on counts and days
        // Week: spam per day
        var expectedThisWeekPerDay = (double)trends.ThisWeekSpamCount / trends.DaysInThisWeek;
        Assert.That(trends.ThisWeekPerDay, Is.EqualTo(expectedThisWeekPerDay).Within(0.01),
            "ThisWeekPerDay should equal ThisWeekSpamCount / DaysInThisWeek");

        // Month: spam per week
        var expectedThisMonthPerWeek = trends.ThisMonthSpamCount / (trends.DaysInThisMonth / 7.0);
        Assert.That(trends.ThisMonthPerWeek, Is.EqualTo(expectedThisMonthPerWeek).Within(0.01),
            "ThisMonthPerWeek should equal ThisMonthSpamCount / (DaysInThisMonth / 7)");

        // Year: spam per month
        var expectedThisYearPerMonth = trends.ThisYearSpamCount / (trends.DaysInThisYear / 30.44);
        Assert.That(trends.ThisYearPerMonth, Is.EqualTo(expectedThisYearPerMonth).Within(0.01),
            "ThisYearPerMonth should equal ThisYearSpamCount / (DaysInThisYear / 30.44)");
    }

    [Test]
    public async Task GetSpamTrendComparison_WeekDifference_CalculatedCorrectly()
    {
        // Act
        var trends = await _analyticsRepository.GetSpamTrendComparisonAsync(DefaultTimeZoneId);

        // Assert
        var expectedDifference = trends.ThisWeekSpamCount - trends.LastWeekSpamCount;
        Assert.That(trends.WeekDifference, Is.EqualTo(expectedDifference),
            "WeekDifference should be ThisWeekSpamCount - LastWeekSpamCount");
    }

    #endregion

    #region GetDetectionAccuracyStatsAsync Tests

    [Test]
    public async Task GetDetectionAccuracyStats_WithFalsePositives_CountsCorrectly()
    {
        // Analytics test data includes 1 false positive (message 82617: spam corrected to ham)
        // Arrange
        var startDate = DateTimeOffset.UtcNow.AddDays(-7);
        var endDate = DateTimeOffset.UtcNow.AddDays(1);

        // Act
        var stats = await _analyticsRepository.GetDetectionAccuracyStatsAsync(
            startDate, endDate, DefaultTimeZoneId);

        // Assert
        Assert.That(stats, Is.Not.Null);
        Assert.That(stats.TotalFalsePositives, Is.GreaterThanOrEqualTo(1),
            "Should have at least 1 false positive from correction data");
    }

    [Test]
    public async Task GetDetectionAccuracyStats_WithFalseNegatives_CountsCorrectly()
    {
        // Analytics test data includes 1 false negative (message 82594: ham corrected to spam)
        // Arrange
        var startDate = DateTimeOffset.UtcNow.AddDays(-7);
        var endDate = DateTimeOffset.UtcNow.AddDays(1);

        // Act
        var stats = await _analyticsRepository.GetDetectionAccuracyStatsAsync(
            startDate, endDate, DefaultTimeZoneId);

        // Assert
        Assert.That(stats, Is.Not.Null);
        Assert.That(stats.TotalFalseNegatives, Is.GreaterThanOrEqualTo(1),
            "Should have at least 1 false negative from correction data");
    }

    [Test]
    public async Task GetDetectionAccuracyStats_TotalDetections_MatchesExpected()
    {
        // Arrange
        var startDate = DateTimeOffset.UtcNow.AddDays(-7);
        var endDate = DateTimeOffset.UtcNow.AddDays(1);

        // Act
        var stats = await _analyticsRepository.GetDetectionAccuracyStatsAsync(
            startDate, endDate, DefaultTimeZoneId);

        // Assert
        Assert.That(stats.TotalDetections, Is.GreaterThan(0), "Should have detections");
        // Total should be at least base ham (2) + analytics spam (5 recent days) = 7+
        Assert.That(stats.TotalDetections, Is.GreaterThanOrEqualTo(
            GoldenDataset.AnalyticsData.BaseHamCount +
            GoldenDataset.AnalyticsData.TodaySpamCount +
            GoldenDataset.AnalyticsData.YesterdaySpamCount));
    }

    [Test]
    public async Task GetDetectionAccuracyStats_PercentageCalculations_Accurate()
    {
        // Arrange
        var startDate = DateTimeOffset.UtcNow.AddDays(-7);
        var endDate = DateTimeOffset.UtcNow.AddDays(1);

        // Act
        var stats = await _analyticsRepository.GetDetectionAccuracyStatsAsync(
            startDate, endDate, DefaultTimeZoneId);

        // Assert - test data guarantees we have detections with 1 FP and 1 FN
        Assert.That(stats.TotalDetections, Is.GreaterThan(0),
            "Test data should provide detections");
        Assert.That(stats.TotalFalsePositives, Is.EqualTo(1),
            "Test data has exactly 1 false positive (82617 corrected to ham)");
        Assert.That(stats.TotalFalseNegatives, Is.EqualTo(1),
            "Test data has exactly 1 false negative (82594 corrected to spam)");

        // Percentages should be valid and non-zero
        Assert.That(stats.FalsePositivePercentage, Is.GreaterThan(0).And.LessThan(100),
            "FP percentage should be positive but less than 100%");
        Assert.That(stats.FalseNegativePercentage, Is.GreaterThan(0).And.LessThan(100),
            "FN percentage should be positive but less than 100%");
    }

    [Test]
    public async Task GetDetectionAccuracyStats_DailyBreakdown_GroupsByUserTimezone()
    {
        // Arrange
        var startDate = DateTimeOffset.UtcNow.AddDays(-7);
        var endDate = DateTimeOffset.UtcNow.AddDays(1);

        // Act
        var stats = await _analyticsRepository.GetDetectionAccuracyStatsAsync(
            startDate, endDate, DefaultTimeZoneId);

        // Assert
        Assert.That(stats.DailyBreakdown, Is.Not.Null);
        // Should have multiple days of data
        Assert.That(stats.DailyBreakdown.Count, Is.GreaterThan(0), "Should have daily breakdown data");

        // Verify daily breakdown is ordered by date
        for (int i = 0; i < stats.DailyBreakdown.Count - 1; i++)
        {
            Assert.That(stats.DailyBreakdown[i].Date, Is.LessThan(stats.DailyBreakdown[i + 1].Date),
                "Daily breakdown should be ordered by date ascending");
        }
    }

    #endregion

    #region GetAlgorithmPerformanceStatsAsync Tests

    [Test]
    public async Task GetAlgorithmPerformanceStats_WithCheckResults_ExtractsTimings()
    {
        // Analytics test data includes check_results_json with ProcessingTimeMs
        // Arrange
        var startDate = DateTimeOffset.UtcNow.AddDays(-7);
        var endDate = DateTimeOffset.UtcNow.AddDays(1);

        // Act
        var stats = await _analyticsRepository.GetAlgorithmPerformanceStatsAsync(
            startDate, endDate, DefaultTimeZoneId);

        // Assert
        Assert.That(stats, Is.Not.Null);
        Assert.That(stats.Count, Is.GreaterThan(0), "Should have algorithm performance data");

        // Each algorithm should have positive timing data
        foreach (var algo in stats)
        {
            Assert.That(algo.TotalExecutions, Is.GreaterThan(0), $"{algo.CheckName} should have executions");
            Assert.That(algo.AverageMs, Is.GreaterThan(0), $"{algo.CheckName} should have positive average time");
        }
    }

    [Test]
    public async Task GetAlgorithmPerformanceStats_MultipleAlgorithms_AggregatesSeparately()
    {
        // Analytics test data has StopWords, Bayes, and OpenAI checks
        // Arrange
        var startDate = DateTimeOffset.UtcNow.AddDays(-7);
        var endDate = DateTimeOffset.UtcNow.AddDays(1);

        // Act
        var stats = await _analyticsRepository.GetAlgorithmPerformanceStatsAsync(
            startDate, endDate, DefaultTimeZoneId);

        // Assert
        var checkNames = stats.Select(s => s.CheckName).ToList();
        Assert.That(checkNames.Count, Is.EqualTo(checkNames.Distinct().Count()),
            "Each algorithm should appear only once (aggregated)");
    }

    [Test]
    public async Task GetAlgorithmPerformanceStats_P95Calculation_Accurate()
    {
        // Arrange
        var startDate = DateTimeOffset.UtcNow.AddDays(-7);
        var endDate = DateTimeOffset.UtcNow.AddDays(1);

        // Act
        var stats = await _analyticsRepository.GetAlgorithmPerformanceStatsAsync(
            startDate, endDate, DefaultTimeZoneId);

        // Assert
        foreach (var algo in stats)
        {
            // P95 should be >= average (since it's a higher percentile)
            Assert.That(algo.P95Ms, Is.GreaterThanOrEqualTo(algo.AverageMs),
                $"{algo.CheckName} P95 should be >= average");
            // P95 should be <= max
            Assert.That(algo.P95Ms, Is.LessThanOrEqualTo(algo.MaxMs),
                $"{algo.CheckName} P95 should be <= max");
        }
    }

    [Test]
    public async Task GetAlgorithmPerformanceStats_EmptyDateRange_ReturnsEmptyList()
    {
        // Arrange - Use date range with no data
        var startDate = DateTimeOffset.UtcNow.AddYears(-10);
        var endDate = DateTimeOffset.UtcNow.AddYears(-9);

        // Act
        var stats = await _analyticsRepository.GetAlgorithmPerformanceStatsAsync(
            startDate, endDate, DefaultTimeZoneId);

        // Assert
        Assert.That(stats, Is.Not.Null);
        Assert.That(stats.Count, Is.EqualTo(0), "Empty date range should return empty list");
    }

    #endregion

    #region GetDetectionMethodComparisonAsync Tests

    [Test]
    public async Task GetDetectionMethodComparison_WithData_ReturnsMethodStats()
    {
        // Arrange
        var startDate = DateTimeOffset.UtcNow.AddDays(-7);
        var endDate = DateTimeOffset.UtcNow.AddDays(1);

        // Act
        var stats = await _analyticsRepository.GetDetectionMethodComparisonAsync(
            startDate, endDate, DefaultTimeZoneId);

        // Assert
        Assert.That(stats, Is.Not.Null);
        Assert.That(stats.Count, Is.GreaterThan(0), "Should have method comparison data");
    }

    [Test]
    public async Task GetDetectionMethodComparison_SpamPercentage_CalculatedCorrectly()
    {
        // Arrange
        var startDate = DateTimeOffset.UtcNow.AddDays(-7);
        var endDate = DateTimeOffset.UtcNow.AddDays(1);

        // Act
        var stats = await _analyticsRepository.GetDetectionMethodComparisonAsync(
            startDate, endDate, DefaultTimeZoneId);

        // Assert - verify we have method stats
        Assert.That(stats.Count, Is.GreaterThan(0), "Should have detection method stats");

        // Each method should have valid percentage calculations
        foreach (var method in stats)
        {
            Assert.That(method.TotalChecks, Is.GreaterThan(0),
                $"{method.MethodName} should have checks from test data");
            Assert.That(method.SpamPercentage, Is.GreaterThanOrEqualTo(0).And.LessThanOrEqualTo(100),
                $"{method.MethodName} spam percentage should be valid 0-100");

            // SpamDetected should not exceed TotalChecks
            Assert.That(method.SpamDetected, Is.LessThanOrEqualTo(method.TotalChecks),
                $"{method.MethodName} spam detected should not exceed total checks");
        }
    }

    [Test]
    public async Task GetDetectionMethodComparison_TracksFPContributions()
    {
        // Analytics test data has 1 FP (message 82617 corrected)
        // Arrange
        var startDate = DateTimeOffset.UtcNow.AddDays(-7);
        var endDate = DateTimeOffset.UtcNow.AddDays(1);

        // Act
        var stats = await _analyticsRepository.GetDetectionMethodComparisonAsync(
            startDate, endDate, DefaultTimeZoneId);

        // Assert
        // At least one method should have contributed to false positives
        var totalFPContributions = stats.Sum(s => s.ContributedToFalsePositives);
        Assert.That(totalFPContributions, Is.GreaterThanOrEqualTo(1),
            "At least one method should have contributed to FP from correction data");
    }

    #endregion

    #region Welcome System Analytics Tests

    [Test]
    public async Task GetWelcomeStatsSummary_WithResponses_CalculatesRates()
    {
        // Analytics test data has 6 welcome responses
        // Arrange
        var startDate = DateTimeOffset.UtcNow.AddDays(-14);
        var endDate = DateTimeOffset.UtcNow.AddDays(1);

        // Act
        var summary = await _analyticsRepository.GetWelcomeStatsSummaryAsync(
            startDate, endDate, DefaultTimeZoneId);

        // Assert
        Assert.That(summary, Is.Not.Null);
        Assert.That(summary.TotalJoins, Is.EqualTo(GoldenDataset.AnalyticsData.TotalWelcomeResponses),
            "Total joins should match test data");
        Assert.That(summary.TotalAccepted, Is.EqualTo(
            GoldenDataset.AnalyticsData.TodayAcceptedCount + GoldenDataset.AnalyticsData.LastWeekAcceptedCount),
            "Total accepted should match test data");
    }

    [Test]
    public async Task GetWelcomeStatsSummary_AcceptanceRate_CalculatedCorrectly()
    {
        // Arrange
        var startDate = DateTimeOffset.UtcNow.AddDays(-14);
        var endDate = DateTimeOffset.UtcNow.AddDays(1);

        // Act
        var summary = await _analyticsRepository.GetWelcomeStatsSummaryAsync(
            startDate, endDate, DefaultTimeZoneId);

        // Assert - use precalculated expected value from test data
        // 3 accepted out of 6 total = 50%
        Assert.That(summary.TotalJoins, Is.EqualTo(GoldenDataset.AnalyticsData.TotalWelcomeResponses));
        Assert.That(summary.AcceptanceRate, Is.EqualTo(GoldenDataset.AnalyticsData.ExpectedAcceptedPercentage),
            "Acceptance rate should be 50% (3 accepted / 6 total)");
    }

    [Test]
    public async Task GetWelcomeStatsSummary_AverageAcceptTime_Calculated()
    {
        // Arrange
        var startDate = DateTimeOffset.UtcNow.AddDays(-14);
        var endDate = DateTimeOffset.UtcNow.AddDays(1);

        // Act
        var summary = await _analyticsRepository.GetWelcomeStatsSummaryAsync(
            startDate, endDate, DefaultTimeZoneId);

        // Assert
        if (summary.TotalAccepted > 0)
        {
            Assert.That(summary.AverageMinutesToAccept, Is.GreaterThan(0),
                "Average accept time should be positive when there are accepted responses");
        }
    }

    [Test]
    public async Task GetWelcomeResponseDistribution_AllTypes_CountsCorrectly()
    {
        // Arrange
        var startDate = DateTimeOffset.UtcNow.AddDays(-14);
        var endDate = DateTimeOffset.UtcNow.AddDays(1);

        // Act
        var distribution = await _analyticsRepository.GetWelcomeResponseDistributionAsync(
            startDate, endDate, DefaultTimeZoneId);

        // Assert
        Assert.That(distribution, Is.Not.Null);
        Assert.That(distribution.TotalResponses, Is.EqualTo(GoldenDataset.AnalyticsData.TotalWelcomeResponses));
        Assert.That(distribution.DeniedCount, Is.EqualTo(GoldenDataset.AnalyticsData.TodayDeniedCount));
        Assert.That(distribution.TimeoutCount, Is.EqualTo(GoldenDataset.AnalyticsData.YesterdayTimeoutCount));
        Assert.That(distribution.LeftCount, Is.EqualTo(GoldenDataset.AnalyticsData.YesterdayLeftCount));
    }

    [Test]
    public async Task GetWelcomeResponseDistribution_Percentages_SumTo100()
    {
        // Arrange
        var startDate = DateTimeOffset.UtcNow.AddDays(-14);
        var endDate = DateTimeOffset.UtcNow.AddDays(1);

        // Act
        var distribution = await _analyticsRepository.GetWelcomeResponseDistributionAsync(
            startDate, endDate, DefaultTimeZoneId);

        // Assert - use precalculated expected percentages
        Assert.That(distribution.TotalResponses, Is.EqualTo(GoldenDataset.AnalyticsData.TotalWelcomeResponses));

        // Verify each percentage against precalculated values
        Assert.That(distribution.AcceptedPercentage, Is.EqualTo(GoldenDataset.AnalyticsData.ExpectedAcceptedPercentage),
            "Accepted percentage should be 50% (3/6)");
        Assert.That(distribution.DeniedPercentage, Is.EqualTo(GoldenDataset.AnalyticsData.ExpectedDeniedPercentage).Within(0.001),
            "Denied percentage should be ~16.67% (1/6)");
        Assert.That(distribution.TimeoutPercentage, Is.EqualTo(GoldenDataset.AnalyticsData.ExpectedTimeoutPercentage).Within(0.001),
            "Timeout percentage should be ~16.67% (1/6)");
        Assert.That(distribution.LeftPercentage, Is.EqualTo(GoldenDataset.AnalyticsData.ExpectedLeftPercentage).Within(0.001),
            "Left percentage should be ~16.67% (1/6)");

        // Verify they sum to 100%
        var totalPercentage = distribution.AcceptedPercentage +
                              distribution.DeniedPercentage +
                              distribution.TimeoutPercentage +
                              distribution.LeftPercentage;
        Assert.That(totalPercentage, Is.EqualTo(100.0).Within(0.001),
            "Percentages should sum to exactly 100%");
    }

    [Test]
    public async Task GetDailyWelcomeJoinTrends_GroupsByDate_InUserTimezone()
    {
        // Arrange
        var startDate = DateTimeOffset.UtcNow.AddDays(-14);
        var endDate = DateTimeOffset.UtcNow.AddDays(1);

        // Act
        var trends = await _analyticsRepository.GetDailyWelcomeJoinTrendsAsync(
            startDate, endDate, DefaultTimeZoneId);

        // Assert
        Assert.That(trends, Is.Not.Null);
        Assert.That(trends.Count, Is.GreaterThan(0), "Should have daily trends");

        // Verify ordered by date ascending
        for (int i = 0; i < trends.Count - 1; i++)
        {
            Assert.That(trends[i].Date, Is.LessThan(trends[i + 1].Date),
                "Trends should be ordered by date ascending");
        }

        // Total join count should match
        var totalJoins = trends.Sum(t => t.JoinCount);
        Assert.That(totalJoins, Is.EqualTo(GoldenDataset.AnalyticsData.TotalWelcomeResponses));
    }

    [Test]
    public async Task GetChatWelcomeStats_GroupsByChatCorrectly()
    {
        // Arrange
        var startDate = DateTimeOffset.UtcNow.AddDays(-14);
        var endDate = DateTimeOffset.UtcNow.AddDays(1);

        // Act
        var chatStats = await _analyticsRepository.GetChatWelcomeStatsAsync(
            startDate, endDate, DefaultTimeZoneId);

        // Assert
        Assert.That(chatStats, Is.Not.Null);
        Assert.That(chatStats.Count, Is.GreaterThan(0), "Should have per-chat stats");

        // All welcome responses are for MainChat
        var mainChatStats = chatStats.FirstOrDefault(c => c.ChatId == GoldenDataset.ManagedChats.MainChat_Id);
        Assert.That(mainChatStats, Is.Not.Null, "Should have stats for main chat");
        Assert.That(mainChatStats!.TotalJoins, Is.EqualTo(GoldenDataset.AnalyticsData.TotalWelcomeResponses));
    }

    [Test]
    public async Task GetChatWelcomeStats_IncludesChatName()
    {
        // Arrange
        var startDate = DateTimeOffset.UtcNow.AddDays(-14);
        var endDate = DateTimeOffset.UtcNow.AddDays(1);

        // Act
        var chatStats = await _analyticsRepository.GetChatWelcomeStatsAsync(
            startDate, endDate, DefaultTimeZoneId);

        // Assert
        foreach (var stat in chatStats)
        {
            Assert.That(stat.ChatName, Is.Not.Null.And.Not.Empty,
                "Chat name should be populated from managed_chats JOIN");
        }
    }

    #endregion

    #region Daily Detection Trends Tests

    [Test]
    public async Task GetDailyDetectionTrends_ReturnsSpamAndHamCounts()
    {
        // Arrange
        var startDate = DateTimeOffset.UtcNow.AddDays(-14);
        var endDate = DateTimeOffset.UtcNow.AddDays(1);

        // Act
        var trends = await _analyticsRepository.GetDailyDetectionTrendsAsync(
            startDate, endDate, DefaultTimeZoneId);

        // Assert
        Assert.That(trends, Is.Not.Null);
        Assert.That(trends.Count, Is.GreaterThan(0), "Should have daily trends");

        // Verify each day has spam and ham counts
        foreach (var day in trends)
        {
            Assert.That(day.SpamCount, Is.GreaterThanOrEqualTo(0));
            Assert.That(day.HamCount, Is.GreaterThanOrEqualTo(0));
        }
    }

    [Test]
    public async Task GetDailyDetectionTrends_OrderedByDateAscending()
    {
        // Arrange
        var startDate = DateTimeOffset.UtcNow.AddDays(-14);
        var endDate = DateTimeOffset.UtcNow.AddDays(1);

        // Act
        var trends = await _analyticsRepository.GetDailyDetectionTrendsAsync(
            startDate, endDate, DefaultTimeZoneId);

        // Assert
        for (int i = 0; i < trends.Count - 1; i++)
        {
            Assert.That(trends[i].Date, Is.LessThan(trends[i + 1].Date),
                "Trends should be ordered by date ascending");
        }
    }

    #endregion

    #region Response Time Stats Tests

    [Test]
    public async Task GetResponseTimeStats_NoUserActions_ReturnsEmptyStats()
    {
        // Analytics test data doesn't include user_actions joined to spam detections
        // Arrange
        var startDate = DateTimeOffset.UtcNow.AddDays(-7);
        var endDate = DateTimeOffset.UtcNow.AddDays(1);

        // Act
        var stats = await _analyticsRepository.GetResponseTimeStatsAsync(
            startDate, endDate, DefaultTimeZoneId);

        // Assert
        Assert.That(stats, Is.Not.Null);
        Assert.That(stats.TotalActions, Is.EqualTo(0),
            "No user actions in test data, so should be 0");
        Assert.That(stats.DailyAverages, Is.Empty);
    }

    #endregion
}
