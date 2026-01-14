using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.PageObjects;

/// <summary>
/// Page object for Users.razor (/users - the Telegram users management page).
/// Provides methods to interact with the tabbed user lists and search functionality.
/// </summary>
public class UsersPage
{
    private readonly IPage _page;

    // Selectors - Layout
    private const string PageTitle = ".mud-typography-h4";
    private const string LoadingIndicator = ".mud-progress-linear";
    private const string SearchInput = ".mud-input input[placeholder*='Search']";
    private const string TotalChip = ".mud-chip:has-text('Total:')";
    private const string FilteredChip = ".mud-chip:has-text('Filtered:')";

    // Selectors - Tabs
    private const string TabContainer = ".mud-tabs";
    private const string TabPanel = ".mud-tab";
    private const string ActiveTab = ".mud-tab-active";
    private const string TabBadge = ".mud-badge";

    // Selectors - Table
    private const string UserTable = ".mud-table";
    private const string TableBody = ".mud-table-body";
    private const string TableRow = ".mud-table-body tr";
    private const string TablePager = ".mud-table-pagination";

    // Selectors - Row content
    private const string UserCell = "td[data-label='User']";
    private const string StatusCell = "td[data-label='Status']";
    private const string ChatsCell = "td[data-label='Chats']";
    private const string WarningsCell = "td[data-label='Warnings']";
    private const string ActionsCell = "td[data-label='Actions']";

    // Action buttons
    private const string ViewDetailsButton = "button[aria-label*='View'], button:has(.mud-icon-root)";
    private const string TrustButton = "button:has([data-testid='VerifiedUserIcon']), button:has([data-testid='PersonOffIcon'])";

    public UsersPage(IPage page)
    {
        _page = page;
    }

    /// <summary>
    /// Navigates to the users page.
    /// </summary>
    public async Task NavigateAsync()
    {
        await _page.GotoAsync("/users");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>
    /// Waits for the page to fully load.
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
            .Select(text => text!.Trim().Split('\n')[0].Trim()) // Extract just the tab name (before any badge number)
            .ToList();
    }

    /// <summary>
    /// Clicks a tab by its name and waits for it to become active.
    /// </summary>
    public async Task SelectTabAsync(string tabName)
    {
        var tab = _page.GetByRole(AriaRole.Tab, new() { Name = tabName });
        await tab.ClickAsync();

        // Wait for the tab to actually become active (aria-selected="true")
        await Expect(tab).ToHaveAttributeAsync("aria-selected", "true", new() { Timeout = 10000 });
    }

    /// <summary>
    /// Gets the currently active tab name.
    /// </summary>
    public async Task<string?> GetActiveTabNameAsync()
    {
        var activeTab = _page.Locator(ActiveTab);
        return await activeTab.TextContentAsync();
    }

    /// <summary>
    /// Searches for users using the search input.
    /// </summary>
    public async Task SearchUsersAsync(string searchText)
    {
        var searchInput = _page.Locator(SearchInput);
        await searchInput.ClearAsync();
        await searchInput.FillAsync(searchText);
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>
    /// Clears the search input by clicking the MudBlazor clear button (X icon).
    /// This triggers OnClearButtonClick which calls ApplyFilters().
    /// </summary>
    public async Task ClearSearchAsync()
    {
        // MudBlazor's Clearable button is an icon button inside the input adornment
        // Use multiple selector strategies for robustness across environments
        var clearButton = _page.Locator(".mud-input-adornment-end button, .mud-input-control button.mud-icon-button").First;

        // Wait for the clear button to be visible (it only shows when input has text)
        await Expect(clearButton).ToBeVisibleAsync();
        await clearButton.ClickAsync();
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>
    /// Gets the total user count from the chip display.
    /// </summary>
    public async Task<int> GetTotalUserCountAsync()
    {
        var chipText = await _page.Locator(TotalChip).TextContentAsync();
        if (string.IsNullOrEmpty(chipText)) return 0;

        // Extract number from "Total: 123"
        var parts = chipText.Split(':');
        if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out var count))
            return count;
        return 0;
    }

    /// <summary>
    /// Checks if the filtered count chip is visible.
    /// </summary>
    public async Task<bool> IsFilteredChipVisibleAsync()
    {
        return await _page.Locator(FilteredChip).IsVisibleAsync();
    }

    /// <summary>
    /// Gets the count of users displayed in the current table.
    /// </summary>
    public async Task<int> GetDisplayedUserCountAsync()
    {
        return await _page.Locator(TableRow).CountAsync();
    }

    /// <summary>
    /// Gets the display names of all visible users in the current table.
    /// </summary>
    public async Task<List<string>> GetUserDisplayNamesAsync()
    {
        var names = new List<string>();
        var rows = await _page.Locator(TableRow).AllAsync();

        foreach (var row in rows)
        {
            var nameCell = row.Locator($"{UserCell} .mud-typography-body2");
            var text = await nameCell.TextContentAsync();
            if (!string.IsNullOrEmpty(text))
                names.Add(text.Trim());
        }

        return names;
    }

    /// <summary>
    /// Checks if a user with the given name is displayed.
    /// </summary>
    public async Task<bool> IsUserDisplayedAsync(string displayName)
    {
        var row = _page.Locator(TableRow).Filter(new() { HasText = displayName });
        return await row.IsVisibleAsync();
    }

    /// <summary>
    /// Gets the status of a user by their display name.
    /// </summary>
    public async Task<string?> GetUserStatusAsync(string displayName)
    {
        var row = _page.Locator(TableRow).Filter(new() { HasText = displayName });
        return await row.Locator($"{StatusCell} .mud-chip").TextContentAsync();
    }

    /// <summary>
    /// Gets the chat count for a user by their display name.
    /// </summary>
    public async Task<string?> GetUserChatCountAsync(string displayName)
    {
        var row = _page.Locator(TableRow).Filter(new() { HasText = displayName });
        return await row.Locator(ChatsCell).TextContentAsync();
    }

    /// <summary>
    /// Checks if a user has a trusted indicator.
    /// </summary>
    public async Task<bool> HasTrustedIndicatorAsync(string displayName)
    {
        var row = _page.Locator(TableRow).Filter(new() { HasText = displayName });
        var trustedIcon = row.Locator(".mud-icon-root[data-testid='VerifiedUserIcon']");
        return await trustedIcon.IsVisibleAsync();
    }

    /// <summary>
    /// Checks if a user has an admin indicator.
    /// </summary>
    public async Task<bool> HasAdminIndicatorAsync(string displayName)
    {
        var row = _page.Locator(TableRow).Filter(new() { HasText = displayName });
        var adminIcon = row.Locator(".mud-icon-root[data-testid='ShieldIcon']");
        return await adminIcon.IsVisibleAsync();
    }

    /// <summary>
    /// Clicks the View Details button for a user.
    /// </summary>
    public async Task ClickViewDetailsAsync(string displayName)
    {
        var row = _page.Locator(TableRow).Filter(new() { HasText = displayName });
        var viewButton = row.Locator($"{ActionsCell} button").First;
        await viewButton.ClickAsync();
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>
    /// Checks if the table pager is visible.
    /// </summary>
    public async Task<bool> IsPagerVisibleAsync()
    {
        return await _page.Locator(TablePager).IsVisibleAsync();
    }

    /// <summary>
    /// Checks if a dialog is open.
    /// </summary>
    public async Task<bool> IsDialogOpenAsync()
    {
        return await _page.Locator(".mud-dialog").IsVisibleAsync();
    }

    /// <summary>
    /// Closes any open dialog.
    /// </summary>
    public async Task CloseDialogAsync()
    {
        var dialog = _page.Locator(".mud-dialog");
        var closeButton = dialog.Locator("button:has-text('Close')");

        if (await closeButton.IsVisibleAsync())
        {
            await closeButton.ClickAsync();
        }
        else
        {
            await _page.Keyboard.PressAsync("Escape");
        }

        // Wait for dialog to close
        await Expect(dialog).Not.ToBeVisibleAsync();
    }

    /// <summary>
    /// Gets the current URL.
    /// </summary>
    public string CurrentUrl => _page.Url;
}
