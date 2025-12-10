using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.PageObjects;

/// <summary>
/// Page object for Audit.razor (/audit - the audit log page).
/// Provides methods to interact with the Web Admin Log and Telegram Moderation Log tabs.
/// Requires GlobalAdmin or Owner role to access.
/// </summary>
public class AuditLogPage
{
    private readonly IPage _page;

    // Navigation
    private const string BasePath = "/audit";

    // Page elements
    private const string PageTitle = ".mud-typography-h4";
    private const string TabContainer = ".mud-tabs";
    private const string TabPanel = ".mud-tab";
    private const string ActiveTab = ".mud-tab-active";

    // Tables
    private const string WebAdminTable = ".mud-tab-panel:not([hidden]) .mud-table";
    private const string TableBody = ".mud-table-body";
    private const string TableRow = ".mud-table-body tr";
    private const string TablePager = ".mud-table-pagination";
    private const string LoadingIndicator = ".mud-progress-linear";

    // Filter elements
    private const string FilterContainer = ".mud-tab-panel:not([hidden]) .mud-paper";

    public AuditLogPage(IPage page)
    {
        _page = page;
    }

    #region Navigation

    /// <summary>
    /// Navigates to the audit log page.
    /// </summary>
    public async Task NavigateAsync()
    {
        await _page.GotoAsync(BasePath);
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>
    /// Navigates to the audit log page with a specific tab fragment.
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
    /// </summary>
    public async Task<bool> IsPageTitleVisibleAsync()
    {
        return await _page.Locator(PageTitle).IsVisibleAsync();
    }

    /// <summary>
    /// Gets the page title text.
    /// </summary>
    public async Task<string?> GetPageTitleAsync()
    {
        return await _page.Locator(PageTitle).TextContentAsync();
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
    /// MudBlazor tabs use role="tab" for accessibility and aria-selected for state.
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
    /// Gets the currently active tab text.
    /// </summary>
    public async Task<string?> GetActiveTabNameAsync()
    {
        var activeTab = _page.Locator("[role='tab'][aria-selected='true']");
        if (await activeTab.CountAsync() == 0)
            return null;
        return await activeTab.First.TextContentAsync();
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
    /// Checks if the Web Admin Log tab is active.
    /// </summary>
    public Task<bool> IsWebAdminLogTabActiveAsync() => IsTabActiveAsync("Web Admin Log");

    /// <summary>
    /// Checks if the Telegram Moderation Log tab is active.
    /// </summary>
    public Task<bool> IsModerationLogTabActiveAsync() => IsTabActiveAsync("Telegram Moderation Log");

    #endregion

    #region Web Admin Log Tab

    /// <summary>
    /// Checks if the Web Admin Log table is visible.
    /// </summary>
    public async Task<bool> IsWebAdminLogTableVisibleAsync()
    {
        return await _page.Locator(WebAdminTable).IsVisibleAsync();
    }

    /// <summary>
    /// Gets the count of rows in the currently visible table.
    /// </summary>
    public async Task<int> GetTableRowCountAsync()
    {
        var rows = _page.Locator($".mud-tab-panel:not([hidden]) {TableRow}");
        return await rows.CountAsync();
    }

    /// <summary>
    /// Gets the header text of columns in the table.
    /// </summary>
    public async Task<List<string>> GetTableHeadersAsync()
    {
        var headerCells = await _page.Locator(".mud-tab-panel:not([hidden]) .mud-table-head th").AllAsync();
        var textTasks = headerCells.Select(cell => cell.TextContentAsync());
        var texts = await Task.WhenAll(textTasks);

        return texts
            .Where(text => !string.IsNullOrEmpty(text))
            .Select(text => text!.Trim())
            .ToList();
    }

    /// <summary>
    /// Checks if the Event Type filter select is visible.
    /// MudBlazor renders labels inside .mud-select elements.
    /// </summary>
    public async Task<bool> IsEventTypeFilterVisibleAsync()
    {
        return await _page.Locator(".mud-select").Filter(new() { HasText = "Event Type" }).First.IsVisibleAsync();
    }

    /// <summary>
    /// Checks if the Actor filter select is visible.
    /// </summary>
    public async Task<bool> IsActorFilterVisibleAsync()
    {
        return await _page.Locator(".mud-select").Filter(new() { HasText = "Actor (Who)" }).First.IsVisibleAsync();
    }

    /// <summary>
    /// Checks if the Target User filter select is visible.
    /// </summary>
    public async Task<bool> IsTargetUserFilterVisibleAsync()
    {
        return await _page.Locator(".mud-select").Filter(new() { HasText = "Target User" }).First.IsVisibleAsync();
    }

    /// <summary>
    /// Selects an event type filter option.
    /// Uses MudBlazor pattern: .mud-select with label text.
    /// </summary>
    public async Task SelectEventTypeFilterAsync(string eventType)
    {
        var select = _page.Locator(".mud-select").Filter(new() { HasText = "Event Type" }).First;
        await select.ClickAsync();

        // Wait for popover to open
        var popover = _page.Locator(".mud-popover-open");
        await Expect(popover).ToBeVisibleAsync();

        // Select the option - MudBlazor uses .mud-list-item for select options
        var option = popover.Locator(".mud-list-item").Filter(new() { HasText = eventType }).First;
        await option.ClickAsync();

        // Wait for popover to close and data to reload
        await Expect(popover).Not.ToBeVisibleAsync();
        await WaitForLoadAsync();
    }

    /// <summary>
    /// Selects an actor filter option.
    /// </summary>
    public async Task SelectActorFilterAsync(string actorText)
    {
        var select = _page.Locator(".mud-select").Filter(new() { HasText = "Actor (Who)" }).First;
        await select.ClickAsync();

        // Wait for popover to open
        var popover = _page.Locator(".mud-popover-open");
        await Expect(popover).ToBeVisibleAsync();

        // Select the option
        var option = popover.Locator(".mud-list-item").Filter(new() { HasText = actorText }).First;
        await option.ClickAsync();

        // Wait for popover to close and data to reload
        await Expect(popover).Not.ToBeVisibleAsync();
        await WaitForLoadAsync();
    }

    /// <summary>
    /// Clears the Event Type filter by selecting "All Events".
    /// </summary>
    public async Task ClearEventTypeFilterAsync()
    {
        await SelectEventTypeFilterAsync("All Events");
    }

    /// <summary>
    /// Clears the Actor filter by selecting "All Actors".
    /// </summary>
    public async Task ClearActorFilterAsync()
    {
        await SelectActorFilterAsync("All Actors");
    }

    /// <summary>
    /// Gets the event type text from a specific row.
    /// </summary>
    public async Task<string?> GetEventTypeFromRowAsync(int rowIndex)
    {
        var row = _page.Locator($".mud-tab-panel:not([hidden]) {TableRow}").Nth(rowIndex);
        var eventTypeCell = row.Locator("td[data-label='Event Type'] .mud-chip");
        return await eventTypeCell.TextContentAsync();
    }

    /// <summary>
    /// Checks if a log entry with specific event type text is visible.
    /// Tests should create deterministic data so only one entry matches.
    /// </summary>
    public async Task<bool> HasLogEntryWithEventTypeAsync(string eventTypeText)
    {
        var chip = _page.Locator($".mud-tab-panel:not([hidden]) td[data-label='Event Type'] .mud-chip:has-text('{eventTypeText}')");
        return await chip.IsVisibleAsync();
    }

    /// <summary>
    /// Checks if a log entry with specific actor text is visible.
    /// Tests should create deterministic data so only one entry matches.
    /// </summary>
    public async Task<bool> HasLogEntryWithActorAsync(string actorText)
    {
        var cell = _page.Locator($".mud-tab-panel:not([hidden]) td[data-label='Actor']:has-text('{actorText}')");
        return await cell.IsVisibleAsync();
    }

    #endregion

    #region Telegram Moderation Log Tab

    /// <summary>
    /// Checks if the Action Type filter select is visible (Moderation Log tab).
    /// </summary>
    public async Task<bool> IsActionTypeFilterVisibleAsync()
    {
        return await _page.Locator(".mud-select").Filter(new() { HasText = "Action Type" }).First.IsVisibleAsync();
    }

    /// <summary>
    /// Checks if the Telegram User ID filter is visible (Moderation Log tab).
    /// MudTextField uses GetByPlaceholder since the label may not be accessible.
    /// </summary>
    public async Task<bool> IsTelegramUserIdFilterVisibleAsync()
    {
        return await _page.GetByPlaceholder("Enter Telegram ID").IsVisibleAsync();
    }

    /// <summary>
    /// Checks if the Issued By filter is visible (Moderation Log tab).
    /// </summary>
    public async Task<bool> IsIssuedByFilterVisibleAsync()
    {
        return await _page.GetByPlaceholder("e.g. system_bot_protection").IsVisibleAsync();
    }

    /// <summary>
    /// Selects an action type filter option.
    /// </summary>
    public async Task SelectActionTypeFilterAsync(string actionType)
    {
        var select = _page.Locator(".mud-select").Filter(new() { HasText = "Action Type" }).First;
        await select.ClickAsync();

        // Wait for popover to open
        var popover = _page.Locator(".mud-popover-open");
        await Expect(popover).ToBeVisibleAsync();

        // Select the option
        var option = popover.Locator(".mud-list-item").Filter(new() { HasText = actionType }).First;
        await option.ClickAsync();

        // Wait for popover to close and data to reload
        await Expect(popover).Not.ToBeVisibleAsync();
        await WaitForLoadAsync();
    }

    /// <summary>
    /// Fills the Telegram User ID filter.
    /// </summary>
    public async Task FilterByTelegramUserIdAsync(string userId)
    {
        var input = _page.GetByPlaceholder("Enter Telegram ID");
        await input.FillAsync(userId);
        await WaitForLoadAsync();
    }

    /// <summary>
    /// Fills the Issued By filter.
    /// </summary>
    public async Task FilterByIssuedByAsync(string issuedBy)
    {
        var input = _page.GetByPlaceholder("e.g. system_bot_protection");
        await input.FillAsync(issuedBy);
        await WaitForLoadAsync();
    }

    /// <summary>
    /// Clears the Action Type filter by selecting "All Actions".
    /// </summary>
    public async Task ClearActionTypeFilterAsync()
    {
        await SelectActionTypeFilterAsync("All Actions");
    }

    /// <summary>
    /// Gets the action type text from a specific row.
    /// </summary>
    public async Task<string?> GetActionTypeFromRowAsync(int rowIndex)
    {
        var row = _page.Locator($".mud-tab-panel:not([hidden]) {TableRow}").Nth(rowIndex);
        var actionTypeCell = row.Locator("td[data-label='Action Type'] .mud-chip");
        return await actionTypeCell.TextContentAsync();
    }

    /// <summary>
    /// Checks if a moderation entry with specific action type text is visible.
    /// </summary>
    public async Task<bool> HasModerationEntryWithActionTypeAsync(string actionTypeText)
    {
        var chip = _page.Locator($".mud-tab-panel:not([hidden]) td[data-label='Action Type'] .mud-chip:has-text('{actionTypeText}')");
        return await chip.IsVisibleAsync();
    }

    /// <summary>
    /// Checks if a moderation entry with specific issued by text is visible.
    /// </summary>
    public async Task<bool> HasModerationEntryWithIssuedByAsync(string issuedByText)
    {
        var cell = _page.Locator($".mud-tab-panel:not([hidden]) td[data-label='Issued By']:has-text('{issuedByText}')");
        return await cell.IsVisibleAsync();
    }

    #endregion

    #region Table Pagination

    /// <summary>
    /// Checks if the table pager is visible.
    /// </summary>
    public async Task<bool> IsPagerVisibleAsync()
    {
        return await _page.Locator($".mud-tab-panel:not([hidden]) {TablePager}").IsVisibleAsync();
    }

    /// <summary>
    /// Clicks the refresh button for the current tab.
    /// </summary>
    public async Task ClickRefreshAsync()
    {
        var refreshButton = _page.Locator(".mud-tab-panel:not([hidden]) button[title*='Refresh']");
        await refreshButton.ClickAsync();
        await WaitForLoadAsync();
    }

    #endregion

    #region Helper Properties

    /// <summary>
    /// Gets the current URL.
    /// </summary>
    public string CurrentUrl => _page.Url;

    /// <summary>
    /// Checks if we're on the audit page.
    /// </summary>
    public bool IsOnAuditPage => _page.Url.Contains("/audit");

    #endregion
}
