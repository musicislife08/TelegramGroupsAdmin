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
    /// </summary>
    public async Task<bool> IsActivityFeedVisibleAsync()
    {
        return await _page.Locator("text=Recent Activity").IsVisibleAsync();
    }

    /// <summary>
    /// Checks if the threshold recommendations alert is visible.
    /// </summary>
    public async Task<bool> IsThresholdRecommendationsAlertVisibleAsync()
    {
        return await _page.Locator(".mud-alert:has-text('threshold recommendation')").IsVisibleAsync();
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
