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

    #region Content Detection Tab

    /// <summary>
    /// A section heading of ContentDetectionAnalytics.razor in the active panel: "Overview",
    /// "Training Data Overview", "Recent Spam Checks", "System Health" or "OpenAI False Positive Prevention"
    /// (each a <c>MudText Typo.h6</c>, so an h6 heading).
    /// </summary>
    public ILocator ContentDetectionHeading(string heading) =>
        _page.Locator(ActiveTabPanel).GetByRole(AriaRole.Heading, new() { Name = heading, Exact = true });

    /// <summary>
    /// The section paper (<c>MudPaper Class="pa-4"</c>) of ContentDetectionAnalytics.razor titled
    /// <paramref name="heading"/>. The cards inside System Health are papers too but carry no pa-4 class
    /// and no section heading, so exactly one element matches.
    /// </summary>
    public ILocator ContentDetectionSection(string heading) =>
        _page.Locator(ActiveTabPanel).Locator(".mud-paper.pa-4")
            .Filter(new() { Has = _page.GetByRole(AriaRole.Heading, new() { Name = heading, Exact = true }) });

    /// <summary>
    /// The h6 value beside an Overview row label ("Total Checks", "Spam Detected", "Stop Words Enabled",
    /// "Admin-Labeled Training Samples"): the label and the value are siblings in one row stack.
    /// </summary>
    public ILocator OverviewValue(string label) =>
        ContentDetectionSection("Overview").GetByText(label, new() { Exact = true }).Locator("..").Locator(".mud-typography-h6");

    /// <summary>
    /// The percentage text beside the progress bar under an Overview row ("Spam Detected", "Stop Words Enabled",
    /// "Admin-Labeled Training Samples"): the bar row is the sibling of the label's row inside the metric stack.
    /// </summary>
    public ILocator OverviewPercentage(string label) =>
        ContentDetectionSection("Overview").GetByText(label, new() { Exact = true }).Locator("../..").Locator(".mud-typography-body2");

    /// <summary>The bold count beside "Confirmed Training Labels" in the Training Data Overview section.</summary>
    public ILocator ConfirmedTrainingLabelsValue =>
        ContentDetectionSection("Training Data Overview").GetByText("Confirmed Training Labels", new() { Exact = true })
            .Locator("..").Locator(".font-weight-bold");

    /// <summary>The System Health card labelled "Stop Words", "Spam Samples", "Training Data" or "Detection Rate".</summary>
    public ILocator HealthCard(string label) =>
        ContentDetectionSection("System Health").Locator(".mud-card")
            .Filter(new() { Has = _page.GetByText(label, new() { Exact = true }) });

    /// <summary>The h6 value of the System Health card labelled <paramref name="label"/>.</summary>
    public ILocator HealthCardValue(string label) => HealthCard(label).Locator(".mud-typography-h6");

    private ILocator VetoSection => ContentDetectionSection("OpenAI False Positive Prevention");

    /// <summary>The veto summary alert: "OpenAI prevented N false positives (P% of detections) in the last 30 days."</summary>
    public ILocator VetoSummaryAlert => VetoSection.Locator(".mud-alert").Filter(new() { HasText = "OpenAI prevented" });

    /// <summary>The veto empty state: "No veto data available for the last 30 days. …"</summary>
    public ILocator VetoEmptyAlert => VetoSection.Locator(".mud-alert").Filter(new() { HasText = "No veto data available" });

    /// <summary>The Algorithm Veto Rates table (the simple table whose first column header is "Algorithm").</summary>
    public ILocator AlgorithmVetoRatesTable =>
        VetoSection.Locator(".mud-simple-table")
            .Filter(new() { Has = _page.GetByRole(AriaRole.Columnheader, new() { Name = "Algorithm", Exact = true }) });

    /// <summary>The body rows of <see cref="AlgorithmVetoRatesTable"/>, one per vetoed algorithm.</summary>
    public ILocator AlgorithmVetoRateRows => AlgorithmVetoRatesTable.Locator("tbody tr");

    /// <summary>
    /// The Algorithm Veto Rates row of <paramref name="algorithm"/> (the CheckName as rendered, e.g. "Bayes").
    /// Cells: Algorithm, Spam Flags, Vetoed by OpenAI, Veto Rate.
    /// </summary>
    public ILocator AlgorithmVetoRateRow(string algorithm) =>
        AlgorithmVetoRateRows.Filter(new() { Has = _page.GetByRole(AriaRole.Cell, new() { Name = algorithm, Exact = true }) });

    /// <summary>The "Recent Vetoed Messages (N)" expansion panel header; the count is every stored veto, unwindowed.</summary>
    public ILocator RecentVetoedMessagesHeader(int count) =>
        VetoSection.GetByText($"Recent Vetoed Messages ({count})", new() { Exact = true });

    /// <summary>The body rows of the Recent Spam Checks table (newest detection first, at most 20).</summary>
    public ILocator RecentSpamCheckRows => ContentDetectionSection("Recent Spam Checks").Locator("tbody tr");

    /// <summary>The Result chip (SPAM / CLEAN) of the Recent Spam Checks row at <paramref name="index"/>.</summary>
    public ILocator RecentSpamCheckResult(int index) => RecentSpamCheckRows.Nth(index).Locator(".mud-chip");

    /// <summary>
    /// A cell of the Recent Spam Checks row at <paramref name="index"/>. Columns: 0 Timestamp, 1 Result,
    /// 2 Score, 3 Reason, 4 User.
    /// </summary>
    public ILocator RecentSpamCheckCell(int index, int column) => RecentSpamCheckRows.Nth(index).Locator("td").Nth(column);

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
