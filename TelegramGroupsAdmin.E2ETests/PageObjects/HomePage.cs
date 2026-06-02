using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.PageObjects;

/// <summary>
/// Page object for Home.razor (/ - the main dashboard page).
/// Displays chat health statistics and quick actions.
/// </summary>
public class HomePage
{
    private readonly IPage _page;

    // Selectors - MudBlazor components
    private const string PageTitle = ".mud-typography-h4";
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

    /// <summary>
    /// Checks if the page is loading.
    /// </summary>
    public async Task<bool> IsLoadingAsync()
    {
        return await _page.Locator(LoadingIndicator).IsVisibleAsync();
    }

    /// <summary>
    /// Gets the page title text.
    /// </summary>
    public async Task<string?> GetPageTitleAsync()
    {
        return await _page.Locator(PageTitle).TextContentAsync();
    }

    /// <summary>
    /// Checks if the stats section is visible.
    /// </summary>
    public async Task<bool> AreStatsVisibleAsync()
    {
        // Stats are in a MudPaper with MudGrid containing stat items
        var statsGrid = _page.Locator(".mud-paper .mud-grid");
        return await statsGrid.IsVisibleAsync();
    }

    /// <summary>
    /// Gets the Total Messages stat value.
    /// Uses Playwright's text-based locator which is more reliable for MudBlazor components.
    /// </summary>
    public async Task<string?> GetTotalMessagesAsync()
    {
        // Find the grid item containing "Total Messages" text, then get the h5 value
        var statItem = _page.Locator(".mud-grid-item").Filter(new() { HasText = "Total Messages" });
        var value = statItem.Locator(".mud-typography-h5");
        return await value.TextContentAsync();
    }

    /// <summary>
    /// Gets the Unique Users stat value.
    /// </summary>
    public async Task<string?> GetUniqueUsersAsync()
    {
        var statItem = _page.Locator(".mud-grid-item").Filter(new() { HasText = "Unique Users" });
        var value = statItem.Locator(".mud-typography-h5");
        return await value.TextContentAsync();
    }

    /// <summary>
    /// Gets the Images stat value.
    /// </summary>
    public async Task<string?> GetImagesCountAsync()
    {
        var statItem = _page.Locator(".mud-grid-item").Filter(new() { HasText = "Images" });
        var value = statItem.Locator(".mud-typography-h5");
        return await value.TextContentAsync();
    }

    /// <summary>
    /// Gets the Data Range stat value.
    /// </summary>
    public async Task<string?> GetDataRangeAsync()
    {
        var statItem = _page.Locator(".mud-grid-item").Filter(new() { HasText = "Data Range" });
        var value = statItem.Locator(".mud-typography-h5");
        return await value.TextContentAsync();
    }

    /// <summary>
    /// Checks if the "View Messages" button is visible.
    /// Uses role-based locator to distinguish from sidebar nav link.
    /// </summary>
    public async Task<bool> IsViewMessagesButtonVisibleAsync()
    {
        // Use GetByRole to find the button specifically (not the nav link)
        return await _page.GetByRole(AriaRole.Link, new() { Name = "View Messages" }).IsVisibleAsync();
    }

    /// <summary>
    /// Clicks the "View Messages" button.
    /// </summary>
    public async Task ClickViewMessagesAsync()
    {
        await _page.GetByRole(AriaRole.Link, new() { Name = "View Messages" }).ClickAsync();
    }

    /// <summary>
    /// Checks if the "Refresh" button is visible.
    /// </summary>
    public async Task<bool> IsRefreshButtonVisibleAsync()
    {
        return await _page.GetByRole(AriaRole.Button, new() { Name = "Refresh" }).IsVisibleAsync();
    }

    /// <summary>
    /// Clicks the "Refresh" button.
    /// </summary>
    public async Task ClickRefreshAsync()
    {
        await _page.GetByRole(AriaRole.Button, new() { Name = "Refresh" }).ClickAsync();
    }

    /// <summary>
    /// Checks if the "no messages" info alert is visible.
    /// </summary>
    public async Task<bool> IsNoMessagesAlertVisibleAsync()
    {
        var alert = _page.Locator(".mud-alert:has-text('hasn\\'t cached any messages')");
        return await alert.IsVisibleAsync();
    }

    /// <summary>
    /// Gets the URL the page navigated to.
    /// </summary>
    public string CurrentUrl => _page.Url;

    #region New Dashboard Stats (#173)

    /// <summary>
    /// Gets the Spam Today stat value.
    /// </summary>
    public async Task<string?> GetSpamTodayAsync()
    {
        var statItem = _page.Locator(".mud-grid-item").Filter(new() { HasText = "Spam Today" });
        var value = statItem.Locator(".mud-typography-h5");
        return await value.TextContentAsync();
    }

    /// <summary>
    /// Gets the Active Bans stat value.
    /// </summary>
    public async Task<string?> GetActiveBansAsync()
    {
        var statItem = _page.Locator(".mud-grid-item").Filter(new() { HasText = "Active Bans" });
        var value = statItem.Locator(".mud-typography-h5");
        return await value.TextContentAsync();
    }

    /// <summary>
    /// Gets the Trusted Users stat value.
    /// </summary>
    public async Task<string?> GetTrustedUsersAsync()
    {
        var statItem = _page.Locator(".mud-grid-item").Filter(new() { HasText = "Trusted Users" });
        var value = statItem.Locator(".mud-typography-h5");
        return await value.TextContentAsync();
    }

    /// <summary>
    /// Gets the Pending Reports count from the dashboard card.
    /// </summary>
    public async Task<string?> GetPendingReportsCountAsync()
    {
        var statItem = _page.Locator(".mud-grid-item").Filter(new() { HasText = "Pending Reports" });
        var value = statItem.Locator(".mud-typography-h5");
        return await value.TextContentAsync();
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

    /// <summary>
    /// Checks if the Recent Activity section is visible.
    /// Only GlobalAdmin/Owner see this panel (gated on _canSeeGlobalStats).
    /// </summary>
    public async Task<bool> IsActivityFeedVisibleAsync()
    {
        return await _page.Locator("text=Recent Activity").IsVisibleAsync();
    }

    /// <summary>
    /// Checks if the Total Messages stat card is visible.
    /// Only GlobalAdmin/Owner see this global stat card (gated on _canSeeGlobalStats).
    /// </summary>
    public async Task<bool> IsTotalMessagesCardVisibleAsync()
    {
        var statItem = _page.Locator(".mud-grid-item").Filter(new() { HasText = "Total Messages" });
        return await statItem.IsVisibleAsync();
    }

    /// <summary>
    /// Checks if the Pending Reports stat card is visible.
    /// This scoped card is shown for all permission tiers (Admin, GlobalAdmin, Owner).
    /// </summary>
    public async Task<bool> IsPendingReportsCardVisibleAsync()
    {
        var statItem = _page.Locator(".mud-grid-item").Filter(new() { HasText = "Pending Reports" });
        return await statItem.IsVisibleAsync();
    }

    // The greyed placeholder rendered in global cards for an Admin shows this
    // caption (Home.razor) instead of a numeric value, plus a Lock icon.
    private const string GlobalAdminOnlyCaption = "GlobalAdmin only";

    // The Recent Activity panel placeholder caption shown for an Admin (Home.razor).
    private const string ActivityPlaceholderCaption = "Requires GlobalAdmin to view";

    /// <summary>
    /// Returns true when the Total Messages card is rendered as the greyed
    /// "GlobalAdmin only" placeholder (Lock icon + caption, no numeric value).
    /// Uses a web-first assertion as the sync point before reading state so the
    /// card has rendered. The data-leak guarantee is that an Admin sees the
    /// caption and NO <c>.mud-typography-h5</c> numeric value.
    /// </summary>
    public async Task<bool> IsTotalMessagesGreyedPlaceholderAsync()
    {
        var card = _page.Locator(".mud-grid-item").Filter(new() { HasText = "Total Messages" });
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 10000 });

        var hasPlaceholderCaption =
            await card.GetByText(GlobalAdminOnlyCaption).CountAsync() > 0;
        var hasNumericValue = await card.Locator(".mud-typography-h5").CountAsync() > 0;

        return hasPlaceholderCaption && !hasNumericValue;
    }

    /// <summary>
    /// Returns the numeric Total Messages value (the <c>.mud-typography-h5</c>
    /// text) when the card shows real data, or <c>null</c> when the card is
    /// rendered as the greyed "GlobalAdmin only" placeholder (no numeric value).
    /// </summary>
    public async Task<string?> GetTotalMessagesValueOrNullAsync()
    {
        var card = _page.Locator(".mud-grid-item").Filter(new() { HasText = "Total Messages" });
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 10000 });

        var value = card.Locator(".mud-typography-h5");
        if (await value.CountAsync() == 0)
        {
            return null;
        }

        return await value.TextContentAsync();
    }

    /// <summary>
    /// Returns true when the Recent Activity panel is rendered as the greyed
    /// placeholder ("Requires GlobalAdmin to view") rather than a real activity
    /// list. Web-first assertion is the sync point before reading state.
    /// </summary>
    public async Task<bool> IsActivityFeedPlaceholderAsync()
    {
        var panel = _page.Locator(".mud-paper").Filter(new() { HasText = "Recent Activity" });
        await Expect(panel).ToBeVisibleAsync(new() { Timeout = 10000 });

        return await panel.GetByText(ActivityPlaceholderCaption).CountAsync() > 0;
    }

    /// <summary>
    /// Gets the count of recent activity items displayed.
    /// </summary>
    public async Task<int> GetActivityFeedItemCountAsync()
    {
        var activitySection = _page.Locator(".mud-paper").Filter(new() { HasText = "Recent Activity" });
        var listItems = activitySection.Locator(".mud-list-item");
        return await listItems.CountAsync();
    }

    /// <summary>
    /// Checks if the "Review Reports" button is visible.
    /// MudButton renders text in uppercase: "REVIEW REPORTS (N)"
    /// </summary>
    public async Task<bool> IsReviewReportsButtonVisibleAsync()
    {
        return await _page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^REVIEW REPORTS", System.Text.RegularExpressions.RegexOptions.IgnoreCase) }).IsVisibleAsync();
    }

    /// <summary>
    /// Clicks the "Review Reports" button.
    /// MudButton renders text in uppercase: "REVIEW REPORTS (N)"
    /// </summary>
    public async Task ClickReviewReportsAsync()
    {
        await _page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^REVIEW REPORTS", System.Text.RegularExpressions.RegexOptions.IgnoreCase) }).ClickAsync();
    }

    #endregion
}
