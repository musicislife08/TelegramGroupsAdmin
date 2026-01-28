using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.PageObjects;

/// <summary>
/// Page object for Analytics.razor (/analytics - the analytics dashboard).
/// Provides methods to interact with the 4 analytics tabs:
/// Content Detection, Message Trends, Performance, and Welcome Analytics.
/// Accessible to all authenticated users.
/// </summary>
public class AnalyticsPage
{
    private readonly IPage _page;

    // Navigation
    private const string BasePath = "/analytics";

    // Page elements
    private const string PageTitle = ".mud-typography-h4";
    private const string TabContainer = ".mud-tabs";
    private const string TabPanel = ".mud-tab";
    private const string ActiveTabPanel = ".mud-tab-panel:not([hidden])";

    public AnalyticsPage(IPage page)
    {
        _page = page;
    }

    #region Navigation

    /// <summary>
    /// Navigates to the analytics page.
    /// </summary>
    public async Task NavigateAsync()
    {
        await _page.GotoAsync(BasePath);
        // Analytics has interactive charts - need Blazor circuit connected
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>
    /// Navigates to the analytics page with a specific tab fragment.
    /// </summary>
    public async Task NavigateToTabAsync(string tabFragment)
    {
        await _page.GotoAsync($"{BasePath}#{tabFragment}");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>
    /// Waits for the page to fully load.
    /// </summary>
    public async Task WaitForLoadAsync()
    {
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    #endregion

    #region Page Title

    /// <summary>
    /// Checks if the page title is visible.
    /// Uses .First because the page may have multiple H4 elements (page title + tab content titles).
    /// </summary>
    public async Task<bool> IsPageTitleVisibleAsync()
    {
        return await _page.Locator(PageTitle).First.IsVisibleAsync();
    }

    /// <summary>
    /// Gets the page title text.
    /// </summary>
    public async Task<string?> GetPageTitleAsync()
    {
        return await _page.Locator(PageTitle).First.TextContentAsync();
    }

    #endregion

    #region Tabs

    /// <summary>
    /// Checks if the tabs container is visible.
    /// </summary>
    public async Task<bool> IsTabsVisibleAsync()
    {
        return await _page.Locator(TabContainer).IsVisibleAsync();
    }

    /// <summary>
    /// Gets the names of all visible tabs.
    /// </summary>
    public async Task<List<string>> GetTabNamesAsync()
    {
        var tabs = await _page.Locator(TabPanel).AllAsync();
        var textTasks = tabs.Select(tab => tab.TextContentAsync());
        var texts = await Task.WhenAll(textTasks);

        return texts
            .Where(text => !string.IsNullOrEmpty(text))
            .Select(text => text!.Trim())
            .ToList();
    }

    /// <summary>
    /// Clicks a tab by its text content and waits for it to become active.
    /// MudBlazor tabs use aria-selected for state.
    /// </summary>
    public async Task SelectTabAsync(string tabName)
    {
        var tab = _page.GetByRole(AriaRole.Tab, new() { Name = tabName });
        await tab.ClickAsync();

        // Wait for the tab to actually become active (aria-selected="true")
        // MudBlazor updates this attribute asynchronously after the click
        await Expect(tab).ToHaveAttributeAsync("aria-selected", "true", new() { Timeout = 10000 });

        await WaitForLoadAsync();
    }

    /// <summary>
    /// Checks if a tab with the given name is active.
    /// </summary>
    private async Task<bool> IsTabActiveAsync(string tabName)
    {
        var tab = _page.GetByRole(AriaRole.Tab, new() { Name = tabName });
        var ariaSelected = await tab.GetAttributeAsync("aria-selected");
        return ariaSelected == "true";
    }

    /// <summary>
    /// Checks if the Content Detection tab is active.
    /// </summary>
    public Task<bool> IsContentDetectionTabActiveAsync() => IsTabActiveAsync("Content Detection");

    /// <summary>
    /// Checks if the Message Trends tab is active.
    /// </summary>
    public Task<bool> IsMessageTrendsTabActiveAsync() => IsTabActiveAsync("Message Trends");

    /// <summary>
    /// Checks if the Performance tab is active.
    /// </summary>
    public Task<bool> IsPerformanceTabActiveAsync() => IsTabActiveAsync("Performance");

    /// <summary>
    /// Checks if the Welcome Analytics tab is active.
    /// </summary>
    public Task<bool> IsWelcomeAnalyticsTabActiveAsync() => IsTabActiveAsync("Welcome Analytics");

    #endregion

    #region Content Detection Tab

    /// <summary>
    /// Checks if the Content Detection analytics component is visible.
    /// </summary>
    public async Task<bool> IsContentDetectionAnalyticsVisibleAsync()
    {
        // ContentDetectionAnalytics component should be in the active tab panel
        var panel = _page.Locator(ActiveTabPanel);
        // Look for characteristic elements of the content detection analytics
        var hasContent = await panel.Locator(".mud-chart, .mud-table, .mud-card, .mud-paper").First.IsVisibleAsync();
        return hasContent;
    }

    #endregion

    #region Message Trends Tab

    /// <summary>
    /// Checks if the Message Trends component is visible.
    /// </summary>
    public async Task<bool> IsMessageTrendsVisibleAsync()
    {
        var panel = _page.Locator(ActiveTabPanel);
        var hasContent = await panel.Locator(".mud-chart, .mud-table, .mud-card, .mud-paper").First.IsVisibleAsync();
        return hasContent;
    }

    #endregion

    #region Trend Cards (Message Trends Tab)

    /// <summary>
    /// Gets the trend card value (percentage or difference) for Week over Week.
    /// </summary>
    public async Task<string?> GetWeekOverWeekValueAsync()
    {
        var card = _page.Locator(".mud-card").Filter(new() { HasText = "Week over Week" });
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 10000 });
        return await card.Locator(".mud-typography-h4").TextContentAsync();
    }

    /// <summary>
    /// Gets the trend card average text for Week over Week (e.g., "4.6 → 6.9/day").
    /// </summary>
    public async Task<string?> GetWeekOverWeekAverageAsync()
    {
        var card = _page.Locator(".mud-card").Filter(new() { HasText = "Week over Week" });
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 10000 });
        return await card.Locator(".mud-typography-caption").Last.TextContentAsync();
    }

    /// <summary>
    /// Gets the trend card value (percentage or difference) for Month over Month.
    /// </summary>
    public async Task<string?> GetMonthOverMonthValueAsync()
    {
        var card = _page.Locator(".mud-card").Filter(new() { HasText = "Month over Month" });
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 10000 });
        return await card.Locator(".mud-typography-h4").TextContentAsync();
    }

    /// <summary>
    /// Gets the trend card average text for Month over Month (e.g., "22.8 → 33.3/week").
    /// </summary>
    public async Task<string?> GetMonthOverMonthAverageAsync()
    {
        var card = _page.Locator(".mud-card").Filter(new() { HasText = "Month over Month" });
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 10000 });
        return await card.Locator(".mud-typography-caption").Last.TextContentAsync();
    }

    /// <summary>
    /// Gets the trend card value (percentage or difference) for Year over Year.
    /// </summary>
    public async Task<string?> GetYearOverYearValueAsync()
    {
        var card = _page.Locator(".mud-card").Filter(new() { HasText = "Year over Year" });
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 10000 });
        return await card.Locator(".mud-typography-h4").TextContentAsync();
    }

    /// <summary>
    /// Gets the trend card average text for Year over Year (e.g., "0 → 5.3/month").
    /// </summary>
    public async Task<string?> GetYearOverYearAverageAsync()
    {
        var card = _page.Locator(".mud-card").Filter(new() { HasText = "Year over Year" });
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 10000 });
        return await card.Locator(".mud-typography-caption").Last.TextContentAsync();
    }

    /// <summary>
    /// Checks if the Week card average shows "/day" suffix.
    /// </summary>
    public async Task<bool> WeekCardShowsPerDayAsync()
    {
        var avg = await GetWeekOverWeekAverageAsync();
        return avg?.Contains("/day") == true;
    }

    /// <summary>
    /// Checks if the Month card average shows "/week" suffix.
    /// </summary>
    public async Task<bool> MonthCardShowsPerWeekAsync()
    {
        var avg = await GetMonthOverMonthAverageAsync();
        return avg?.Contains("/week") == true;
    }

    /// <summary>
    /// Checks if the Year card average shows "/month" suffix.
    /// </summary>
    public async Task<bool> YearCardShowsPerMonthAsync()
    {
        var avg = await GetYearOverYearAverageAsync();
        return avg?.Contains("/month") == true;
    }

    /// <summary>
    /// Asserts all three trend cards are visible.
    /// Uses Expect for auto-waiting since cards load via async database call.
    /// Throws if any card is not visible within the timeout.
    /// </summary>
    public async Task AssertTrendCardsVisibleAsync()
    {
        var weekCard = _page.Locator(".mud-card").Filter(new() { HasText = "Week over Week" });
        var monthCard = _page.Locator(".mud-card").Filter(new() { HasText = "Month over Month" });
        var yearCard = _page.Locator(".mud-card").Filter(new() { HasText = "Year over Year" });

        await Expect(weekCard).ToBeVisibleAsync(new() { Timeout = 10000 });
        await Expect(monthCard).ToBeVisibleAsync();
        await Expect(yearCard).ToBeVisibleAsync();
    }

    #endregion

    #region Performance Tab

    /// <summary>
    /// Checks if the Performance metrics component is visible.
    /// The Performance tab has a specific info alert.
    /// </summary>
    public async Task<bool> IsPerformanceMetricsVisibleAsync()
    {
        var panel = _page.Locator(ActiveTabPanel);
        // Performance tab has an info alert about global statistics
        var hasInfoAlert = await panel.Locator(".mud-alert:has-text('Performance metrics')").IsVisibleAsync();
        return hasInfoAlert;
    }

    #endregion

    #region Welcome Analytics Tab

    /// <summary>
    /// Checks if the Welcome Analytics component is visible.
    /// The Welcome Analytics tab has a specific info alert.
    /// </summary>
    public async Task<bool> IsWelcomeAnalyticsVisibleAsync()
    {
        var panel = _page.Locator(ActiveTabPanel);
        // Welcome tab has an info alert about welcome system metrics
        var hasInfoAlert = await panel.Locator(".mud-alert:has-text('Welcome system metrics')").IsVisibleAsync();
        return hasInfoAlert;
    }

    #endregion

    #region Helper Properties

    /// <summary>
    /// Gets the current URL.
    /// </summary>
    public string CurrentUrl => _page.Url;

    /// <summary>
    /// Checks if we're on the analytics page.
    /// </summary>
    public bool IsOnAnalyticsPage => _page.Url.Contains("/analytics");

    /// <summary>
    /// Gets the current URL fragment (e.g., "detection", "trends").
    /// </summary>
    public string? GetCurrentFragment()
    {
        var uri = new Uri(_page.Url);
        return uri.Fragment.TrimStart('#');
    }

    #endregion
}
