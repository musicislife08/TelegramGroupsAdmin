using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using TelegramGroupsAdmin.Components.Shared.Analytics;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Models.Analytics;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.ComponentTests.Components.Analytics;

/// <summary>
/// Custom test context for MessageTrends component.
/// Wires up mocked IMessageStatsService, IManagedChatsRepository, IAnalyticsRepository,
/// cascading WebUserIdentity and TimeZoneInfo parameters, and MudBlazor services.
/// </summary>
public abstract class MessageTrendsTestContext : BunitContext
{
    protected IMessageStatsService MessageStatsService { get; }
    protected IManagedChatsRepository ChatsRepository { get; }
    protected IAnalyticsRepository AnalyticsRepository { get; }
    protected ISnackbar Snackbar { get; }

    protected MessageTrendsTestContext()
    {
        MessageStatsService = Substitute.For<IMessageStatsService>();
        ChatsRepository = Substitute.For<IManagedChatsRepository>();
        AnalyticsRepository = Substitute.For<IAnalyticsRepository>();
        Snackbar = Substitute.For<ISnackbar>();

        // Register mocks BEFORE AddMudServices to ensure our mock takes precedence
        Services.AddSingleton(MessageStatsService);
        Services.AddSingleton(ChatsRepository);
        Services.AddSingleton(AnalyticsRepository);
        Services.AddSingleton(Snackbar);

        Services.AddMudServices(options =>
        {
            options.PopoverOptions.ThrowOnDuplicateProvider = false;
            options.PopoverOptions.CheckForPopoverProvider = false;
        });

        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupVoid("mudPopover.initialize", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudPopover.connect", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudPopover.disconnect", _ => true).SetVoidResult();
        JSInterop.Setup<int>("mudpopoverHelper.countProviders").SetResult(1);

        // Cascade WebUserIdentity — component returns early in OnInitializedAsync if null
        RenderTree.TryAdd<CascadingValue<WebUserIdentity?>>(p =>
            p.Add(cv => cv.Value, WebUserRenderHelper.TestWebUser));

        // Cascade TimeZoneInfo — component uses (UserTimeZone ?? TimeZoneInfo.Utc).Id
        RenderTree.TryAdd<CascadingValue<TimeZoneInfo?>>(p =>
            p.Add(cv => cv.Value, TimeZoneInfo.Utc));
    }

    /// <summary>
    /// Configures the default mock returns for all services.
    /// Call this in each test's arrange phase to set up the expected data.
    /// </summary>
    protected void ConfigureMocks(
        WeekOverWeekGrowth? growth = null)
    {
        // No accessible chats — avoids rendering the chat filter chip set
        ChatsRepository.GetUserAccessibleChatsAsync(
                Arg.Any<string>(),
                Arg.Any<PermissionLevel>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<ManagedChatRecord>());

        // Spam trend comparison — default-constructed record
        AnalyticsRepository.GetSpamTrendComparisonAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new SpamTrendComparison());

        // Message trends with configurable WeekOverWeekGrowth
        var trendsData = new MessageTrendsData
        {
            TotalMessages = 1000,
            DailyAverage = 100.0,
            UniqueUsers = 50,
            SpamPercentage = 2.5,
            WeekOverWeekGrowth = growth
        };

        MessageStatsService.GetMessageTrendsAsync(
                Arg.Any<List<long>>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(trendsData);
    }

    /// <summary>
    /// Creates a WeekOverWeekGrowth record with HasPreviousPeriod = true and distinct
    /// MessageGrowthPercent / DailyAverageGrowthPercent values for field differentiation tests.
    /// </summary>
    protected static WeekOverWeekGrowth GrowthWithPreviousPeriod(
        double messageGrowthPercent = 15.0,
        double dailyAverageGrowthPercent = 25.0,
        double userGrowthPercent = 10.0,
        double spamGrowthPercent = 5.0) =>
        new()
        {
            HasPreviousPeriod = true,
            MessageGrowthPercent = messageGrowthPercent,
            DailyAverageGrowthPercent = dailyAverageGrowthPercent,
            UserGrowthPercent = userGrowthPercent,
            SpamGrowthPercent = spamGrowthPercent
        };
}

/// <summary>
/// Component tests for the four overview metric card chips in MessageTrends.razor.
///
/// Verifies that:
/// - Chips show on 7-day and 30-day views when HasPreviousPeriod is true
/// - Chips are hidden on All Time view regardless of HasPreviousPeriod
/// - Chips are hidden when HasPreviousPeriod is false
/// - The Daily Average card uses DailyAverageGrowthPercent (not MessageGrowthPercent)
///
/// These tests close the FRONT-01 gap identified in 08-VERIFICATION.md.
/// </summary>
[TestFixture]
public class MessageTrendsOverviewCardTests : MessageTrendsTestContext
{
    [SetUp]
    public void Setup()
    {
        MessageStatsService.ClearReceivedCalls();
        ChatsRepository.ClearReceivedCalls();
        AnalyticsRepository.ClearReceivedCalls();
    }

    [Test]
    public async Task ChipsVisible_WhenLast7Days_AndHasPreviousPeriod()
    {
        // Arrange — growth available; component defaults to 30d on init
        ConfigureMocks(growth: GrowthWithPreviousPeriod());
        var cut = Render<MessageTrends>();

        // Wait for the initial 30d load to complete
        cut.WaitForState(() => cut.Markup.Contains("Total Messages"), timeout: TimeSpan.FromSeconds(5));

        // Act — click the "Last 7 Days" button to switch to 7d view
        var last7DaysButton = cut.FindAll("button").First(b => b.TextContent.Contains("Last 7 Days"));
        await last7DaysButton.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        // Wait for the 7d load to complete
        cut.WaitForState(() => cut.Markup.Contains("Total Messages"), timeout: TimeSpan.FromSeconds(5));

        // Assert — all four cards should contain growth chips (mud-chip elements with arrow text)
        var markup = cut.Markup;
        Assert.That(markup, Does.Contain("mud-chip"), "Expected growth chips to be visible in 7d view");
        Assert.That(markup, Does.Contain("↑"), "Expected upward arrow in growth chip for 7d view");
    }

    [Test]
    public void ChipsVisible_WhenLast30Days_AndHasPreviousPeriod()
    {
        // Arrange — growth available; component loads 30d on OnInitializedAsync
        ConfigureMocks(growth: GrowthWithPreviousPeriod());
        var cut = Render<MessageTrends>();

        // Wait for the initial 30d load to complete (no button click needed — this is the default)
        cut.WaitForState(() => cut.Markup.Contains("Total Messages"), timeout: TimeSpan.FromSeconds(5));

        // Assert — chips should be visible in the 30d view (default state after init)
        var markup = cut.Markup;
        Assert.That(markup, Does.Contain("mud-chip"), "Expected growth chips to be visible in 30d view");
        Assert.That(markup, Does.Contain("↑"), "Expected upward arrow in growth chip for 30d view");
    }

    [Test]
    public async Task ChipsHidden_WhenAllTime()
    {
        // Arrange — growth data available but _currentView = "all" guard should hide chips
        ConfigureMocks(growth: GrowthWithPreviousPeriod());
        var cut = Render<MessageTrends>();

        // Wait for the initial 30d load to complete
        cut.WaitForState(() => cut.Markup.Contains("Total Messages"), timeout: TimeSpan.FromSeconds(5));

        // Act — click "All Time" button
        var allTimeButton = cut.FindAll("button").First(b => b.TextContent.Contains("All Time"));
        await allTimeButton.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        // Wait for the All Time load to complete
        cut.WaitForState(() => cut.Markup.Contains("Total Messages"), timeout: TimeSpan.FromSeconds(5));

        // Assert — no growth chips should be visible (the _currentView != "all" guard hides them)
        var markup = cut.Markup;
        // The chip arrows should not be present since All Time hides growth chips entirely
        Assert.That(markup, Does.Not.Contain("↑ 15.0%"), "Expected message growth chip to be hidden on All Time view");
        Assert.That(markup, Does.Not.Contain("↑ 25.0%"), "Expected daily average growth chip to be hidden on All Time view");
        Assert.That(markup, Does.Not.Contain("↑ 10.0%"), "Expected user growth chip to be hidden on All Time view");
    }

    [Test]
    public void DailyAverageCard_UsesDailyAverageGrowthPercent()
    {
        // Arrange — MessageGrowthPercent = 10.0%, DailyAverageGrowthPercent = 25.0%
        // If the bug were present, Daily Average card would show "↑ 10.0%" (MessageGrowthPercent)
        // With the fix, it shows "↑ 25.0%" (DailyAverageGrowthPercent)
        ConfigureMocks(growth: GrowthWithPreviousPeriod(
            messageGrowthPercent: 10.0,
            dailyAverageGrowthPercent: 25.0));
        var cut = Render<MessageTrends>();

        // Wait for the initial 30d load to complete
        cut.WaitForState(() => cut.Markup.Contains("Daily Average"), timeout: TimeSpan.FromSeconds(5));

        var markup = cut.Markup;

        // Assert — "↑ 25.0%" must appear (DailyAverageGrowthPercent)
        Assert.That(markup, Does.Contain("↑ 25.0%"),
            "Expected Daily Average card to display DailyAverageGrowthPercent (25.0%), not MessageGrowthPercent (10.0%)");
    }

    [Test]
    public void ChipsHidden_WhenNoPreviousPeriod()
    {
        // Arrange — HasPreviousPeriod = false; no growth data available for comparison
        ConfigureMocks(growth: new WeekOverWeekGrowth
        {
            HasPreviousPeriod = false,
            MessageGrowthPercent = 15.0,
            DailyAverageGrowthPercent = 25.0,
            UserGrowthPercent = 10.0,
            SpamGrowthPercent = 5.0
        });
        var cut = Render<MessageTrends>();

        // Wait for the initial 30d load to complete
        cut.WaitForState(() => cut.Markup.Contains("Total Messages"), timeout: TimeSpan.FromSeconds(5));

        // Assert — no growth chips rendered even though _currentView = "30d"
        // The HasPreviousPeriod == true guard prevents chip rendering
        var markup = cut.Markup;
        Assert.That(markup, Does.Not.Contain("↑ 15.0%"),
            "Expected growth chips to be hidden when HasPreviousPeriod is false");
        Assert.That(markup, Does.Not.Contain("↑ 25.0%"),
            "Expected growth chips to be hidden when HasPreviousPeriod is false");
    }

    [Test]
    public void NoNullReferenceException_WhenGrowthIsNull()
    {
        // Arrange — growth is null (no comparison data available)
        ConfigureMocks(growth: null);

        // Act — should render without throwing NullReferenceException
        var cut = Render<MessageTrends>();

        // Wait for the initial 30d load to complete
        cut.WaitForState(() => cut.Markup.Contains("Total Messages"), timeout: TimeSpan.FromSeconds(5));

        // Assert — component rendered successfully, no growth chips visible
        var markup = cut.Markup;
        Assert.That(markup, Does.Contain("Total Messages"),
            "Component should render successfully with null growth");
        Assert.That(markup, Does.Not.Contain("↑"),
            "No growth arrows should appear when growth is null");
        Assert.That(markup, Does.Not.Contain("↓"),
            "No growth arrows should appear when growth is null");
    }
}
