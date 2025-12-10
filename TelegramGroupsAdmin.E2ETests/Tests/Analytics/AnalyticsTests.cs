using Microsoft.Playwright;
using TelegramGroupsAdmin.E2ETests.Infrastructure;
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
        Assert.That(await _analyticsPage.IsTabsVisibleAsync(), Is.True,
            "Tab container should be visible for Admin");
    }

    [Test]
    public async Task Analytics_PageLoads_ForGlobalAdmin()
    {
        // Arrange - login as GlobalAdmin
        await LoginAsGlobalAdminAsync();

        // Act - navigate to analytics page
        await _analyticsPage.NavigateAsync();

        // Assert - page loads successfully (tabs visible)
        Assert.That(await _analyticsPage.IsTabsVisibleAsync(), Is.True,
            "Tab container should be visible for GlobalAdmin");
    }

    [Test]
    public async Task Analytics_PageLoads_ForOwner()
    {
        // Arrange - login as Owner
        await LoginAsOwnerAsync();

        // Act - navigate to analytics page
        await _analyticsPage.NavigateAsync();

        // Assert - page loads successfully (tabs visible)
        Assert.That(await _analyticsPage.IsTabsVisibleAsync(), Is.True,
            "Tab container should be visible for Owner");
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
        var tabNames = await _analyticsPage.GetTabNamesAsync();

        Assert.That(tabNames, Does.Contain("Content Detection"),
            "Should have Content Detection tab");
        Assert.That(tabNames, Does.Contain("Message Trends"),
            "Should have Message Trends tab");
        Assert.That(tabNames, Does.Contain("Performance"),
            "Should have Performance tab");
        Assert.That(tabNames, Does.Contain("Welcome Analytics"),
            "Should have Welcome Analytics tab");
    }

    [Test]
    public async Task Analytics_ContentDetectionTab_IsDefaultActive()
    {
        // Arrange - login as Owner
        await LoginAsOwnerAsync();

        // Act - navigate to analytics page (no fragment)
        await _analyticsPage.NavigateAsync();

        // Assert - Content Detection tab is active by default
        Assert.That(await _analyticsPage.IsContentDetectionTabActiveAsync(), Is.True,
            "Content Detection tab should be active by default");
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
        Assert.That(await _analyticsPage.IsMessageTrendsTabActiveAsync(), Is.True,
            "Message Trends tab should be active after clicking");

        // URL should have fragment
        Assert.That(_analyticsPage.GetCurrentFragment(), Is.EqualTo("trends"),
            "URL fragment should be 'trends'");
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
        Assert.That(await _analyticsPage.IsPerformanceTabActiveAsync(), Is.True,
            "Performance tab should be active after clicking");
        Assert.That(await _analyticsPage.IsPerformanceMetricsVisibleAsync(), Is.True,
            "Performance metrics info alert should be visible");
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
        Assert.That(await _analyticsPage.IsWelcomeAnalyticsTabActiveAsync(), Is.True,
            "Welcome Analytics tab should be active after clicking");
        Assert.That(await _analyticsPage.IsWelcomeAnalyticsVisibleAsync(), Is.True,
            "Welcome Analytics info alert should be visible");
    }

    [Test]
    public async Task Analytics_FragmentNavigation_DirectToTab()
    {
        // Arrange - login as Owner
        await LoginAsOwnerAsync();

        // Act - navigate directly to performance tab via fragment
        await _analyticsPage.NavigateToTabAsync("performance");

        // Assert - Performance tab is active
        Assert.That(await _analyticsPage.IsPerformanceTabActiveAsync(), Is.True,
            "Performance tab should be active when navigating with #performance fragment");
    }

    [Test]
    public async Task Analytics_FragmentNavigation_WelcomeTab()
    {
        // Arrange - login as Owner
        await LoginAsOwnerAsync();

        // Act - navigate directly to welcome tab via fragment
        await _analyticsPage.NavigateToTabAsync("welcome");

        // Assert - Welcome Analytics tab is active
        Assert.That(await _analyticsPage.IsWelcomeAnalyticsTabActiveAsync(), Is.True,
            "Welcome Analytics tab should be active when navigating with #welcome fragment");
    }

    #endregion
}
