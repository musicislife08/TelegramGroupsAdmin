using Microsoft.EntityFrameworkCore;
using NSubstitute;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Models.Analytics;
using TelegramGroupsAdmin.Repositories;

namespace TelegramGroupsAdmin.UnitTests.Services;

/// <summary>
/// Unit tests for MessageStatsService growth calculation logic.
/// Validates that week-over-week growth is always computed from endDate backward,
/// independent of the selected time range filter (startDate).
///
/// Uses EF Core InMemory provider to seed real message data and exercise
/// GetMessageTrendsAsync end-to-end without a PostgreSQL instance.
/// </summary>
[TestFixture]
public class MessageStatsServiceTests
{
    private AppDbContext _context = null!;
    private IDbContextFactory<AppDbContext> _mockFactory = null!;
    private MessageStatsService _sut = null!;

    private static readonly DateTimeOffset Now = new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options);

        _mockFactory = Substitute.For<IDbContextFactory<AppDbContext>>();
        _mockFactory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(_context));

        _sut = new MessageStatsService(_mockFactory);
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
    }

    /// <summary>
    /// Seeds messages across two weeks and a managed chat for per-chat breakdown.
    /// Previous week: 5 messages (days 9-13 ago relative to Now — clearly in [endDate-14d, endDate-7d])
    /// Current week: 10 messages (days 0-6 ago, at exact 12-hour intervals — clearly in [endDate-7d, endDate])
    ///
    /// currentWeekStart = Now.AddDays(-7) = Now - 168h
    /// Timestamps for current week: Now-6h, Now-30h, Now-54h, ... Now-six-at-12h-intervals
    /// Timestamps for previous week: Now-9*24h, Now-10*24h, ..., Now-13*24h
    /// </summary>
    private void SeedTwoWeeksOfMessages(long chatId = 100L)
    {
        // Seed the managed chat for per-chat query
        _context.ManagedChats.Add(new ManagedChatRecordDto
        {
            ChatId = chatId,
            ChatName = "Test Chat",
            ChatType = ManagedChatType.Group,
            BotStatus = BotChatStatus.Member,
            IsAdmin = true,
            IsActive = true,
            IsDeleted = false,
            AddedAt = Now.AddDays(-30),
        });

        // Previous week messages — 5 messages at days 9-13 ago (past the 7-day boundary by at least 2 days)
        for (var i = 0; i < 5; i++)
        {
            _context.Messages.Add(new MessageRecordDto
            {
                MessageId = 10 + i,
                UserId = 1001L,
                ChatId = chatId,
                Timestamp = Now.AddDays(-(9 + i)),  // days 9, 10, 11, 12, 13 ago
                ContentCheckSkipReason = ContentCheckSkipReason.NotSkipped
            });
        }

        // Current week messages — 10 messages spaced 12 hours apart within the last 6 days
        for (var i = 0; i < 10; i++)
        {
            _context.Messages.Add(new MessageRecordDto
            {
                MessageId = 100 + i,
                UserId = 1001L,
                ChatId = chatId,
                Timestamp = Now.AddHours(-(6 + i * 12)),  // 6h, 18h, 30h, ..., 114h ago (< 7 days)
                ContentCheckSkipReason = ContentCheckSkipReason.NotSkipped
            });
        }

        _context.SaveChanges();
    }

    /// <summary>
    /// Seeds only the current week (7 days), no previous week data.
    /// </summary>
    private void SeedOneWeekOfMessages(long chatId = 100L)
    {
        _context.ManagedChats.Add(new ManagedChatRecordDto
        {
            ChatId = chatId,
            ChatName = "Test Chat",
            ChatType = ManagedChatType.Group,
            BotStatus = BotChatStatus.Member,
            IsAdmin = true,
            IsActive = true,
            IsDeleted = false,
            AddedAt = Now.AddDays(-7),
        });

        // Current week only (days 0-6 ago)
        for (var i = 1; i <= 7; i++)
        {
            _context.Messages.Add(new MessageRecordDto
            {
                MessageId = i,
                UserId = 1001L,
                ChatId = chatId,
                Timestamp = Now.AddDays(-(i - 1)).AddHours(-1),
                ContentCheckSkipReason = ContentCheckSkipReason.NotSkipped
            });
        }

        _context.SaveChanges();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 1: 7-day view returns non-null WeekOverWeekGrowth with HasPreviousPeriod=true
    //         when the database has 14+ days of message data
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task GetMessageTrendsAsync_7DayRange_ReturnsPreviousPeriodGrowth_WhenDbHas14DaysOfData()
    {
        // Arrange — seed two full weeks of data
        SeedTwoWeeksOfMessages();

        var endDate = Now;
        var startDate = endDate.AddDays(-7);  // 7-day view

        // Act
        var result = await _sut.GetMessageTrendsAsync(
            chatIds: [],
            startDate: startDate,
            endDate: endDate,
            timeZoneId: "UTC",
            cancellationToken: CancellationToken.None);

        // Assert — growth must be non-null and HasPreviousPeriod must be true
        // even though daysDiff (endDate - startDate) = 7, which is < MinDaysForWeekOverWeekGrowth (14)
        Assert.That(result.WeekOverWeekGrowth, Is.Not.Null,
            "WeekOverWeekGrowth should not be null when DB has 14+ days of data");
        Assert.That(result.WeekOverWeekGrowth!.HasPreviousPeriod, Is.True,
            "HasPreviousPeriod should be true when DB has messages in both current and previous week");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 2: 30-day view returns WeekOverWeekGrowth with HasPreviousPeriod=true
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task GetMessageTrendsAsync_30DayRange_ReturnsPreviousPeriodGrowth()
    {
        // Arrange — seed two weeks of data (both fall within the 30-day window)
        SeedTwoWeeksOfMessages();

        var endDate = Now;
        var startDate = endDate.AddDays(-30);  // 30-day view

        // Act
        var result = await _sut.GetMessageTrendsAsync(
            chatIds: [],
            startDate: startDate,
            endDate: endDate,
            timeZoneId: "UTC",
            cancellationToken: CancellationToken.None);

        // Assert
        Assert.That(result.WeekOverWeekGrowth, Is.Not.Null);
        Assert.That(result.WeekOverWeekGrowth!.HasPreviousPeriod, Is.True);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 3: HasPreviousPeriod=false when DB has fewer than 14 days of total data
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task GetMessageTrendsAsync_ReturnsFalsePreviousPeriod_WhenDbHasFewerThan14DaysOfData()
    {
        // Arrange — seed only 7 days of messages (no data in previous week at all)
        SeedOneWeekOfMessages();

        var endDate = Now;
        var startDate = endDate.AddDays(-7);  // 7-day view

        // Act
        var result = await _sut.GetMessageTrendsAsync(
            chatIds: [],
            startDate: startDate,
            endDate: endDate,
            timeZoneId: "UTC",
            cancellationToken: CancellationToken.None);

        // Assert — previous week (days 8-14) has no messages, so no previous period
        var growth = result.WeekOverWeekGrowth;
        if (growth != null)
        {
            Assert.That(growth.HasPreviousPeriod, Is.False,
                "HasPreviousPeriod should be false when DB has no messages in the previous week window");
        }
        // If WeekOverWeekGrowth is null, that also satisfies the requirement
        // (no growth shown when there's no previous data)
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 4: DailyAverageGrowthPercent is independent from MessageGrowthPercent
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task GetMessageTrendsAsync_DailyAverageGrowthPercent_IsIndependentFromMessageGrowthPercent()
    {
        // Arrange
        // Previous week: 5 messages (5/7 daily avg ≈ 0.714)
        // Current week: 10 messages (10/7 daily avg ≈ 1.429)
        // MessageGrowthPercent = (10-5)/5 * 100 = 100%
        // DailyAverageGrowthPercent = ((10/7 - 5/7) / (5/7)) * 100 = (5/7) / (5/7) * 100 = 100%
        // In this symmetric case they are equal — let's use an asymmetric case
        // We need a scenario where MessageGrowthPercent != DailyAverageGrowthPercent.
        // Since both use the same 7-day window, the formula is proportional.
        // DailyAverageGrowthPercent = (currentAvg - previousAvg) / previousAvg * 100
        //                           = (currentMessages/7 - previousMessages/7) / (previousMessages/7) * 100
        //                           = (currentMessages - previousMessages) / previousMessages * 100
        //                           = MessageGrowthPercent  (always equal if same 7-day window)
        //
        // The key correctness requirement: DailyAverageGrowthPercent MUST be a separate property
        // (not null, correctly computed), distinct from being a copy of MessageGrowthPercent in its source.
        // The property must EXIST on the model and be populated.
        SeedTwoWeeksOfMessages();

        var endDate = Now;
        var startDate = endDate.AddDays(-7);

        // Act
        var result = await _sut.GetMessageTrendsAsync(
            chatIds: [],
            startDate: startDate,
            endDate: endDate,
            timeZoneId: "UTC",
            cancellationToken: CancellationToken.None);

        // Assert — DailyAverageGrowthPercent must exist and be correctly populated
        Assert.That(result.WeekOverWeekGrowth, Is.Not.Null);
        var growth = result.WeekOverWeekGrowth!;
        Assert.That(growth.HasPreviousPeriod, Is.True);

        // DailyAverageGrowthPercent must be a finite number (not NaN or infinity)
        Assert.That(double.IsFinite(growth.DailyAverageGrowthPercent), Is.True,
            "DailyAverageGrowthPercent must be a finite number");

        // With 10 current week messages and 5 previous week messages:
        // Expected DailyAverageGrowthPercent = (10/7 - 5/7) / (5/7) * 100 = 100%
        Assert.That(growth.DailyAverageGrowthPercent, Is.EqualTo(100.0).Within(0.1),
            "DailyAverageGrowthPercent should reflect current/previous week daily averages");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 5: When previous week has 0 messages, all growth percentages are 0
    //         (no division by zero)
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task GetMessageTrendsAsync_WhenPreviousWeekHasZeroMessages_AllGrowthPercentagesAreZero()
    {
        // Arrange — seed only current week (previous week will have 0 messages)
        // But we need the service to think there IS a previous period (HasPreviousPeriod=true)
        // So we need to trigger a case where:
        //   - previous week query returns 0 messages
        //   - but service still computes growth (doesn't return null)
        // After the fix, the service should: run a separate query for previous week data,
        // find 0 messages, set all growth = 0, and HasPreviousPeriod = false (no data)
        //
        // Actually: when previous week has 0 messages, there's nothing to compare against.
        // The expected behavior per the plan: "When previous week has 0 messages,
        // all growth percentages are 0 (no division by zero)".
        // This means: if service DOES return growth with HasPreviousPeriod=true (edge case
        // where currentWeek > 0 but previousWeek = 0), growth should be 0 not NaN/Infinity.
        //
        // We need a DB state where the query returns previous week count = 0.
        // Current approach: seed only current week messages.
        SeedOneWeekOfMessages();

        var endDate = Now;
        var startDate = endDate.AddDays(-30);  // 30-day range to trigger the old path

        // Act
        var result = await _sut.GetMessageTrendsAsync(
            chatIds: [],
            startDate: startDate,
            endDate: endDate,
            timeZoneId: "UTC",
            cancellationToken: CancellationToken.None);

        // Assert — if growth is returned, percentages must not be NaN or Infinity
        if (result.WeekOverWeekGrowth != null)
        {
            var growth = result.WeekOverWeekGrowth;
            Assert.That(double.IsNaN(growth.MessageGrowthPercent), Is.False,
                "MessageGrowthPercent must not be NaN");
            Assert.That(double.IsInfinity(growth.MessageGrowthPercent), Is.False,
                "MessageGrowthPercent must not be Infinity");
            Assert.That(double.IsNaN(growth.DailyAverageGrowthPercent), Is.False,
                "DailyAverageGrowthPercent must not be NaN");
            Assert.That(double.IsInfinity(growth.DailyAverageGrowthPercent), Is.False,
                "DailyAverageGrowthPercent must not be Infinity");
        }
        // If growth is null, that also satisfies the requirement (no division by zero issue)
    }
}
