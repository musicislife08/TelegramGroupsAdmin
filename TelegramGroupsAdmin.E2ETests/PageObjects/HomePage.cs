using Microsoft.Playwright;

namespace TelegramGroupsAdmin.E2ETests.PageObjects;

/// <summary>
/// Page object for Home.razor (/ - the main dashboard page).
/// Displays chat health statistics and quick actions.
/// </summary>
public class HomePage
{
    private readonly IPage _page;

    // Selectors - MudBlazor components
    private const string PageTitleSelector = ".mud-typography-h4";
    private const string LoadingIndicator = ".mud-progress-linear";

    public HomePage(IPage page)
    {
        _page = page;
    }

    /// <summary>
    /// Navigates to the home/dashboard page.
    /// </summary>
    public async Task NavigateAsync()
    {
        await _page.GotoAsync("/");
        // Dashboard has interactive stats - need Blazor circuit connected
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>
    /// Waits for the page to fully load (stats loaded, loading indicator gone).
    /// </summary>
    public async Task WaitForLoadAsync(int timeoutMs = 15000)
    {
        // Wait for loading indicator to disappear
        await _page.Locator(LoadingIndicator).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = timeoutMs
        });
    }

    /// <summary>The page title.</summary>
    public ILocator PageTitle => _page.Locator(PageTitleSelector);

    /// <summary>The stats section: a MudPaper with a MudGrid of stat items.</summary>
    public ILocator StatsGrid => _page.Locator(".mud-paper .mud-grid");

    /// <summary>The stat card (grid item) whose label is <paramref name="label"/>, e.g. "Total Messages".</summary>
    public ILocator StatCard(string label) => _page.Locator(".mud-grid-item").Filter(new() { HasText = label });

    /// <summary>
    /// The numeric value (h5) of the stat card labelled <paramref name="label"/>.
    /// Absent when the card is the greyed "GlobalAdmin only" placeholder.
    /// </summary>
    public ILocator StatValue(string label) => StatCard(label).Locator(".mud-typography-h5");

    /// <summary>
    /// The "GlobalAdmin only" caption inside the stat card labelled <paramref name="label"/>, shown
    /// instead of a numeric value when an Admin views a global card.
    /// </summary>
    public ILocator StatPlaceholderCaption(string label) => StatCard(label).GetByText(GlobalAdminOnlyCaption);

    /// <summary>
    /// The "View Messages" quick action. Role-based to distinguish it from the sidebar nav link.
    /// </summary>
    public ILocator ViewMessagesButton => _page.GetByRole(AriaRole.Link, new() { Name = "View Messages" });

    /// <summary>The "Refresh" button.</summary>
    public ILocator RefreshButton => _page.GetByRole(AriaRole.Button, new() { Name = "Refresh" });

    /// <summary>
    /// The "Review Reports" button. MudButton renders text in uppercase: "REVIEW REPORTS (N)".
    /// </summary>
    public ILocator ReviewReportsButton => _page.GetByRole(AriaRole.Button, new()
    {
        NameRegex = new System.Text.RegularExpressions.Regex("^REVIEW REPORTS", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
    });

    /// <summary>The info alert shown when no messages are cached.</summary>
    public ILocator NoMessagesAlert => _page.Locator(".mud-alert:has-text('hasn\\'t cached any messages')");

    /// <summary>
    /// The Recent Activity heading. The panel renders for every tier; for an Admin it is a placeholder.
    /// </summary>
    public ILocator ActivityFeedHeading => _page.Locator("text=Recent Activity");

    /// <summary>The Recent Activity panel.</summary>
    public ILocator ActivityFeedPanel => _page.Locator(".mud-paper").Filter(new() { HasText = "Recent Activity" });

    /// <summary>The activity items listed in the Recent Activity panel.</summary>
    public ILocator ActivityFeedItems => ActivityFeedPanel.Locator(".mud-list-item");

    /// <summary>
    /// The "Requires GlobalAdmin to view" caption shown in place of the activity list for an Admin.
    /// </summary>
    public ILocator ActivityFeedPlaceholderCaption => ActivityFeedPanel.GetByText(ActivityPlaceholderCaption);

    /// <summary>
    /// Clicks the "View Messages" button.
    /// </summary>
    public async Task ClickViewMessagesAsync()
    {
        await ViewMessagesButton.ClickAsync();
    }

    /// <summary>
    /// Clicks the "Refresh" button.
    /// </summary>
    public async Task ClickRefreshAsync()
    {
        await RefreshButton.ClickAsync();
    }

    /// <summary>
    /// Clicks the Pending Reports card to navigate to /reports.
    /// Always navigates regardless of pending count.
    /// </summary>
    public async Task ClickPendingReportsCardAsync()
    {
        var card = _page.Locator(".mud-card").Filter(new() { HasText = "Pending Reports" });
        await card.ClickAsync();
    }

    // The greyed placeholder rendered in global cards for an Admin shows this
    // caption (Home.razor) instead of a numeric value, plus a Lock icon.
    private const string GlobalAdminOnlyCaption = "GlobalAdmin only";

    // The Recent Activity panel placeholder caption shown for an Admin (Home.razor).
    private const string ActivityPlaceholderCaption = "Requires GlobalAdmin to view";
}
