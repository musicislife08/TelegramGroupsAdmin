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
    private const string PageTitleSelector = ".mud-typography-h4";
    private const string TabContainer = ".mud-tabs";
    private const string TabPanel = ".mud-tab";
    private const string ActiveTabPanel = ".mud-tab-panel:not([hidden])";
    private const string DisabledClass = "mud-disabled";

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
        // Analytics has interactive charts - need the Blazor circuit live
        await _page.WaitForInteractiveAsync();
    }

    /// <summary>
    /// Navigates to the analytics page with a specific tab fragment.
    /// </summary>
    public async Task NavigateToTabAsync(string tabFragment)
    {
        await _page.GotoAsync($"{BasePath}#{tabFragment}");
        await _page.WaitForInteractiveAsync();
    }

    /// <summary>
    /// Waits for the page to fully load.
    /// </summary>
    public async Task WaitForLoadAsync()
    {
        await _page.WaitForInteractiveAsync();
    }

    #endregion

    #region Page Title

    /// <summary>
    /// The page title.
    /// Uses .First because the page may have multiple H4 elements (page title + tab content titles).
    /// </summary>
    public ILocator PageTitle => _page.Locator(PageTitleSelector).First;

    #endregion

    #region Tabs

    /// <summary>The tabs container.</summary>
    public ILocator TabsContainer => _page.Locator(TabContainer);

    /// <summary>Every tab header (<c>.mud-tab</c>) in the tab bar.</summary>
    public ILocator Tabs => _page.Locator(TabPanel);

    /// <summary>
    /// The clickable tab element (button) for the given tab name.
    /// MudBlazor renders each tab as a <c>.mud-tab</c> element with role="tab" and marks the
    /// active one with aria-selected="true"; a disabled <c>MudTabPanel</c> adds the
    /// <c>mud-disabled</c> class to this element.
    /// </summary>
    public ILocator Tab(string tabName) =>
        _page.GetByRole(AriaRole.Tab, new() { Name = tabName });

    /// <summary>
    /// Clicks a tab by its text content and waits for it to become active.
    /// MudBlazor tabs use aria-selected for state.
    /// </summary>
    public async Task SelectTabAsync(string tabName)
    {
        var tab = Tab(tabName);
        await tab.ClickAsync();

        // Wait for the tab to actually become active (aria-selected="true")
        // MudBlazor updates this attribute asynchronously after the click
        await Expect(tab).ToHaveAttributeAsync("aria-selected", "true", new() { Timeout = 10000 });

        await WaitForLoadAsync();
    }

    /// <summary>
    /// Asserts the tab with the given name is rendered but disabled/greyed
    /// (<c>mud-disabled</c> class on the <c>.mud-tab</c> element).
    /// </summary>
    public async Task ExpectTabDisabledAsync(string tabName)
    {
        var tab = Tab(tabName);
        await Expect(tab).ToBeVisibleAsync(new() { Timeout = 10000 });
        await Expect(tab).ToContainClassAsync(DisabledClass);
    }

    /// <summary>
    /// Asserts the tab with the given name is rendered and enabled (no <c>mud-disabled</c> class).
    /// The tab is asserted visible first so the absence check cannot pass before it renders.
    /// </summary>
    public async Task ExpectTabEnabledAsync(string tabName)
    {
        var tab = Tab(tabName);
        await Expect(tab).ToBeVisibleAsync(new() { Timeout = 10000 });
        await Expect(tab).Not.ToContainClassAsync(DisabledClass);
    }

    #endregion

    #region Tab Content

    /// <summary>
    /// The first characteristic content element (chart, table, card or paper) in the active tab panel,
    /// e.g. the Content Detection or Message Trends analytics component.
    /// </summary>
    public ILocator ActiveTabContent =>
        _page.Locator(ActiveTabPanel).Locator(".mud-chart, .mud-table, .mud-card, .mud-paper").First;

    #endregion

    #region Trend Cards (Message Trends Tab)

    /// <summary>
    /// The trend card titled <paramref name="title"/>: "Week over Week", "Month over Month" or "Year over Year".
    /// </summary>
    public ILocator TrendCard(string title) => _page.Locator(".mud-card").Filter(new() { HasText = title });

    /// <summary>The trend card value (percentage or difference) of the card titled <paramref name="title"/>.</summary>
    public ILocator TrendCardValue(string title) => TrendCard(title).Locator(".mud-typography-h4");

    /// <summary>
    /// The trend card average text of the card titled <paramref name="title"/>
    /// (e.g., "4.6 → 6.9/day", "22.8 → 33.3/week", "0 → 5.3/month").
    /// </summary>
    public ILocator TrendCardAverage(string title) => TrendCard(title).Locator(".mud-typography-caption").Last;

    /// <summary>
    /// Asserts all three trend cards are visible.
    /// Uses Expect for auto-waiting since cards load via async database call.
    /// Throws if any card is not visible within the timeout.
    /// </summary>
    public async Task AssertTrendCardsVisibleAsync()
    {
        await Expect(TrendCard("Week over Week")).ToBeVisibleAsync(new() { Timeout = 10000 });
        await Expect(TrendCard("Month over Month")).ToBeVisibleAsync();
        await Expect(TrendCard("Year over Year")).ToBeVisibleAsync();
    }

    #endregion

    #region Performance Tab

    /// <summary>
    /// The Performance tab's info alert about global statistics.
    /// </summary>
    public ILocator PerformanceMetricsAlert =>
        _page.Locator(ActiveTabPanel).Locator(".mud-alert:has-text('Performance metrics')");

    #endregion

    #region Welcome Analytics Tab

    /// <summary>
    /// The Welcome Analytics tab's info alert about welcome system metrics.
    /// </summary>
    public ILocator WelcomeAnalyticsAlert =>
        _page.Locator(ActiveTabPanel).Locator(".mud-alert:has-text('Welcome system metrics')");

    #endregion

    #region Helper Properties

    /// <summary>
    /// Gets the current URL.
    /// </summary>
    public string CurrentUrl => _page.Url;

    #endregion
}
