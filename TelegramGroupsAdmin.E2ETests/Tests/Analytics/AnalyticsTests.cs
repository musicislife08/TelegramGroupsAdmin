using System.Text.RegularExpressions;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Analytics;

/// <summary>
/// Tests for the Analytics page (/analytics).
/// Verifies tab navigation and that all analytics components load.
/// Analytics is accessible to all authenticated users (Admin, GlobalAdmin, Owner).
/// Uses SharedAuthenticatedTestBase for faster test execution with shared factory.
/// </summary>
[TestFixture]
public class AnalyticsTests : SharedAuthenticatedTestBase
{
    private AnalyticsPage _analyticsPage = null!;

    [SetUp]
    public void SetUp()
    {
        _analyticsPage = new AnalyticsPage(Page);
    }

    #region Access Control Tests

    [Test]
    public async Task Analytics_PageLoads_ForAdmin()
    {
        // Arrange - login as Admin (lowest permission level)
        await LoginAsAdminAsync();

        // Act - navigate to analytics page
        await _analyticsPage.NavigateAsync();

        // Assert - page loads successfully for Admin (tabs visible)
        await Expect(_analyticsPage.TabsContainer).ToBeVisibleAsync();
    }

    [Test]
    public async Task Analytics_PageLoads_ForGlobalAdmin()
    {
        // Arrange - login as GlobalAdmin
        await LoginAsGlobalAdminAsync();

        // Act - navigate to analytics page
        await _analyticsPage.NavigateAsync();

        // Assert - page loads successfully (tabs visible)
        await Expect(_analyticsPage.TabsContainer).ToBeVisibleAsync();
    }

    [Test]
    public async Task Analytics_PageLoads_ForOwner()
    {
        // Arrange - login as Owner
        await LoginAsOwnerAsync();

        // Act - navigate to analytics page
        await _analyticsPage.NavigateAsync();

        // Assert - page loads successfully (tabs visible)
        await Expect(_analyticsPage.TabsContainer).ToBeVisibleAsync();
    }

    #endregion

    #region Tab Structure Tests

    [Test]
    public async Task Analytics_HasAllExpectedTabs()
    {
        // Arrange - login as Owner
        await LoginAsOwnerAsync();

        // Act - navigate to analytics page
        await _analyticsPage.NavigateAsync();

        // Assert - verify all 4 tabs exist
        await Expect(_analyticsPage.Tab("Content Detection")).ToBeVisibleAsync();
        await Expect(_analyticsPage.Tab("Message Trends")).ToBeVisibleAsync();
        await Expect(_analyticsPage.Tab("Performance")).ToBeVisibleAsync();
        await Expect(_analyticsPage.Tab("Welcome Analytics")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Analytics_Admin_SeesAllTabs_GlobalOnesDisabled()
    {
        // Arrange - login as Admin (chat-scoped permission)
        await LoginAsAdminAsync();

        // Act - navigate to analytics page
        await _analyticsPage.NavigateAsync();

        // Assert - interim cross-chat-leak UX: all four tab shells render, but the
        // three global tabs are DISABLED/greyed (mud-disabled) so an Admin cannot
        // open them and no global data component mounts. Message Trends is the only
        // enabled tab and is forced active for Admin.
        // Admin should still see all four tab shells.
        await Expect(_analyticsPage.Tabs).ToHaveCountAsync(4);
        await Expect(_analyticsPage.Tab("Content Detection")).ToBeVisibleAsync();
        await Expect(_analyticsPage.Tab("Message Trends")).ToBeVisibleAsync();
        await Expect(_analyticsPage.Tab("Performance")).ToBeVisibleAsync();
        await Expect(_analyticsPage.Tab("Welcome Analytics")).ToBeVisibleAsync();

        // The three global tabs must be disabled (mud-disabled on the .mud-tab element).
        await _analyticsPage.ExpectTabDisabledAsync("Content Detection");
        await _analyticsPage.ExpectTabDisabledAsync("Performance");
        await _analyticsPage.ExpectTabDisabledAsync("Welcome Analytics");

        // Message Trends stays enabled and is the active tab for Admin. The disabled checks
        // above confirm the permission-aware render has landed before this absence check.
        await _analyticsPage.ExpectTabEnabledAsync("Message Trends");
        await Expect(_analyticsPage.Tab("Message Trends")).ToHaveAttributeAsync("aria-selected", "true");
    }

    [Test]
    public async Task Analytics_GlobalAdmin_SeesAllFourTabs()
    {
        // Arrange - login as GlobalAdmin
        await LoginAsGlobalAdminAsync();

        // Act - navigate to analytics page
        await _analyticsPage.NavigateAsync();

        // Assert - GlobalAdmin sees all four tabs, all ENABLED (none greyed/disabled).
        await Expect(_analyticsPage.Tabs).ToHaveCountAsync(4);
        await Expect(_analyticsPage.Tab("Content Detection")).ToBeVisibleAsync();
        await Expect(_analyticsPage.Tab("Message Trends")).ToBeVisibleAsync();
        await Expect(_analyticsPage.Tab("Performance")).ToBeVisibleAsync();
        await Expect(_analyticsPage.Tab("Welcome Analytics")).ToBeVisibleAsync();

        // Each "not disabled" check asserts its tab visible first (inside ExpectTabEnabledAsync),
        // and the permission flag is set synchronously before the first render, so the
        // absence check cannot pass before the tab has rendered.
        await _analyticsPage.ExpectTabEnabledAsync("Content Detection");
        await _analyticsPage.ExpectTabEnabledAsync("Message Trends");
        await _analyticsPage.ExpectTabEnabledAsync("Performance");
        await _analyticsPage.ExpectTabEnabledAsync("Welcome Analytics");

        // The Content Detection tab is gated on IsGlobalAdminOrHigher, so a GlobalAdmin gets the real component
        // (its Overview section), not the disabled shell, and it is the selected tab.
        await Expect(_analyticsPage.Tab("Content Detection")).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(_analyticsPage.ContentDetectionHeading("Overview")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Analytics_ContentDetectionTab_IsDefaultActive()
    {
        // Arrange - login as Owner
        await LoginAsOwnerAsync();

        // Act - navigate to analytics page (no fragment)
        await _analyticsPage.NavigateAsync();

        // Assert - Content Detection tab is active by default
        await Expect(_analyticsPage.Tab("Content Detection")).ToHaveAttributeAsync("aria-selected", "true");
    }

    #endregion

    #region Tab Navigation Tests

    [Test]
    public async Task Analytics_TabNavigation_MessageTrends()
    {
        // Arrange - login as Owner
        await LoginAsOwnerAsync();
        await _analyticsPage.NavigateAsync();

        // Act - click Message Trends tab
        await _analyticsPage.SelectTabAsync("Message Trends");

        // Assert - tab is now active
        await Expect(_analyticsPage.Tab("Message Trends")).ToHaveAttributeAsync("aria-selected", "true");

        // URL should have fragment 'trends'
        await Expect(Page).ToHaveURLAsync(new Regex("#trends$"));
    }

    [Test]
    public async Task Analytics_TabNavigation_Performance()
    {
        // Arrange - login as Owner
        await LoginAsOwnerAsync();
        await _analyticsPage.NavigateAsync();

        // Act - click Performance tab
        await _analyticsPage.SelectTabAsync("Performance");

        // Assert - tab is now active and shows info alert
        await Expect(_analyticsPage.Tab("Performance")).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(_analyticsPage.PerformanceMetricsAlert).ToBeVisibleAsync();
    }

    [Test]
    public async Task Analytics_TabNavigation_WelcomeAnalytics()
    {
        // Arrange - login as Owner
        await LoginAsOwnerAsync();
        await _analyticsPage.NavigateAsync();

        // Act - click Welcome Analytics tab
        await _analyticsPage.SelectTabAsync("Welcome Analytics");

        // Assert - tab is now active and shows info alert
        await Expect(_analyticsPage.Tab("Welcome Analytics")).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(_analyticsPage.WelcomeAnalyticsAlert).ToBeVisibleAsync();
    }

    [Test]
    public async Task Analytics_FragmentNavigation_DirectToTab()
    {
        // Arrange - login as Owner
        await LoginAsOwnerAsync();

        // Act - navigate directly to performance tab via fragment
        await _analyticsPage.NavigateToTabAsync("performance");

        // Assert - Performance tab is active
        await Expect(_analyticsPage.Tab("Performance")).ToHaveAttributeAsync("aria-selected", "true");
    }

    [Test]
    public async Task Analytics_FragmentNavigation_WelcomeTab()
    {
        // Arrange - login as Owner
        await LoginAsOwnerAsync();

        // Act - navigate directly to welcome tab via fragment
        await _analyticsPage.NavigateToTabAsync("welcome");

        // Assert - Welcome Analytics tab is active
        await Expect(_analyticsPage.Tab("Welcome Analytics")).ToHaveAttributeAsync("aria-selected", "true");
    }

    #endregion

    #region Message Trends Tab - Spam Trend Cards Tests

    [Test]
    public async Task Analytics_TrendCards_AreVisible()
    {
        // Arrange - login as Owner
        await LoginAsOwnerAsync();

        // Act - navigate to Message Trends tab (trend cards are here, not Content Detection)
        await _analyticsPage.NavigateAsync();
        await _analyticsPage.SelectTabAsync("Message Trends");

        // Assert - all three trend cards should be visible
        await _analyticsPage.AssertTrendCardsVisibleAsync();
    }

    [Test]
    public async Task Analytics_WeekOverWeekCard_ShowsDailyAverage()
    {
        // Arrange - login as Owner
        await LoginAsOwnerAsync();

        // Act - navigate to Message Trends tab
        await _analyticsPage.NavigateAsync();
        await _analyticsPage.SelectTabAsync("Message Trends");

        // Assert - Week card should show average with "/day" suffix
        await Expect(_analyticsPage.TrendCardAverage("Week over Week"))
            .ToContainTextAsync("/day", new() { Timeout = 10000 });
    }

    [Test]
    public async Task Analytics_MonthOverMonthCard_ShowsWeeklyAverage()
    {
        // Arrange - login as Owner
        await LoginAsOwnerAsync();

        // Act - navigate to Message Trends tab
        await _analyticsPage.NavigateAsync();
        await _analyticsPage.SelectTabAsync("Message Trends");

        // Assert - Month card should show average with "/week" suffix
        await Expect(_analyticsPage.TrendCardAverage("Month over Month"))
            .ToContainTextAsync("/week", new() { Timeout = 10000 });
    }

    [Test]
    public async Task Analytics_YearOverYearCard_ShowsMonthlyAverage()
    {
        // Arrange - login as Owner
        await LoginAsOwnerAsync();

        // Act - navigate to Message Trends tab
        await _analyticsPage.NavigateAsync();
        await _analyticsPage.SelectTabAsync("Message Trends");

        // Assert - Year card should show average with "/month" suffix
        await Expect(_analyticsPage.TrendCardAverage("Year over Year"))
            .ToContainTextAsync("/month", new() { Timeout = 10000 });
    }

    [Test]
    public async Task Analytics_TrendCards_HaveValues()
    {
        // Arrange - login as Owner
        await LoginAsOwnerAsync();

        // Act - navigate to Message Trends tab
        await _analyticsPage.NavigateAsync();
        await _analyticsPage.SelectTabAsync("Message Trends");

        // Assert - all trend cards should have some value (percentage or difference)
        await Expect(_analyticsPage.TrendCardValue("Week over Week")).Not.ToBeEmptyAsync(new() { Timeout = 10000 });
        await Expect(_analyticsPage.TrendCardValue("Month over Month")).Not.ToBeEmptyAsync(new() { Timeout = 10000 });
        await Expect(_analyticsPage.TrendCardValue("Year over Year")).Not.ToBeEmptyAsync(new() { Timeout = 10000 });
    }

    [Test]
    public async Task Analytics_TrendCards_HaveAverages()
    {
        // Arrange - login as Owner
        await LoginAsOwnerAsync();

        // Act - navigate to Message Trends tab
        await _analyticsPage.NavigateAsync();
        await _analyticsPage.SelectTabAsync("Message Trends");

        // Assert - all trend cards should have average comparisons displayed
        await Expect(_analyticsPage.TrendCardAverage("Week over Week")).Not.ToBeEmptyAsync(new() { Timeout = 10000 });
        await Expect(_analyticsPage.TrendCardAverage("Month over Month")).Not.ToBeEmptyAsync(new() { Timeout = 10000 });
        await Expect(_analyticsPage.TrendCardAverage("Year over Year")).Not.ToBeEmptyAsync(new() { Timeout = 10000 });
    }

    #endregion
}
