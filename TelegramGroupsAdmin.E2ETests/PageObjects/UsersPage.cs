using System.Text.RegularExpressions;
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
    private const string PageTitleSelector = ".mud-typography-h4";
    private const string SearchInput = ".mud-input input[placeholder*='Search']";

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
    private const string PagerInformationSelector = ".mud-table-page-number-information";

    // Selectors - Row content
    private const string UserCell = "td[data-label='User']";
    private const string StatusCell = "td[data-label='Status']";
    private const string ChatsCell = "td[data-label='Chats']";
    private const string WarningsCell = "td[data-label='Warnings']";
    private const string ActionsCell = "td[data-label='Actions']";

    // Action buttons
    private const string ViewDetailsButton = "button[aria-label*='View'], button:has(.mud-icon-root)";
    private const string TrustButton = "button:has([data-testid='VerifiedUserIcon']), button:has([data-testid='PersonOffIcon'])";

    // Dialog
    private const string DialogSelector = ".mud-dialog";

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
        await _page.WaitForInteractiveAsync();
        await Expect(PageTitle).ToBeVisibleAsync();
    }

    /// <summary>
    /// Waits for the page to fully load.
    /// </summary>
    public async Task WaitForLoadAsync(int timeoutMs = 15000)
    {
        // Wait for the Blazor circuit to be live (required for interactivity)
        await _page.WaitForInteractiveAsync();

        // Wait for tabs to be visible
        await Expect(Tabs).ToBeVisibleAsync(new() { Timeout = timeoutMs });

        // Wait for the first table to render (ServerData loads lazily)
        await Expect(UserTables.First).ToBeVisibleAsync(new() { Timeout = timeoutMs });
    }

    /// <summary>The page title ("Telegram Users").</summary>
    public ILocator PageTitle => _page.Locator(PageTitleSelector);

    /// <summary>The tabs container.</summary>
    public ILocator Tabs => _page.Locator(TabContainer);

    /// <summary>All tab headers, in display order.</summary>
    public ILocator TabHeaders => _page.Locator(TabPanel);

    /// <summary>
    /// The tab header whose text contains <paramref name="tabName"/> (case-insensitive;
    /// MudBlazor renders tab names uppercase, followed by any badge number).
    /// </summary>
    public ILocator TabHeader(string tabName) => TabHeaders.Filter(new() { HasText = tabName });

    /// <summary>The currently active tab header.</summary>
    public ILocator ActiveTabHeader => _page.Locator(ActiveTab);

    /// <summary>
    /// The user tables. Only the active tab's panel is rendered, so this is normally a single table.
    /// </summary>
    public ILocator UserTables => _page.Locator(UserTable);

    /// <summary>The rows of the current user table.</summary>
    public ILocator UserRows => _page.Locator(TableRow);

    /// <summary>The display-name text of every row in the current user table.</summary>
    public ILocator UserDisplayNames => _page.Locator($"{TableRow} {UserCell} .mud-typography-body2");

    /// <summary>The table row for the user whose row text contains <paramref name="displayName"/>.</summary>
    public ILocator UserRow(string displayName) => UserRows.Filter(new() { HasText = displayName });

    /// <summary>The status chip of the user row containing <paramref name="displayName"/>.</summary>
    public ILocator UserStatusChip(string displayName) => UserRow(displayName).Locator($"{StatusCell} .mud-chip");

    /// <summary>The Chats cell of the user row containing <paramref name="displayName"/>.</summary>
    public ILocator UserChatsCell(string displayName) => UserRow(displayName).Locator(ChatsCell);

    /// <summary>The trusted indicator icon in the user row containing <paramref name="displayName"/>.</summary>
    public ILocator TrustedIndicator(string displayName) =>
        UserRow(displayName).Locator(".mud-icon-root[data-testid='VerifiedUserIcon']");

    /// <summary>The admin indicator icon in the user row containing <paramref name="displayName"/>.</summary>
    public ILocator AdminIndicator(string displayName) =>
        UserRow(displayName).Locator(".mud-icon-root[data-testid='ShieldIcon']");

    /// <summary>The table pager.</summary>
    public ILocator Pager => _page.Locator(TablePager);

    /// <summary>The pager information text of the current table, e.g. "1-25 of 42".</summary>
    public ILocator PagerInformation => _page.Locator(PagerInformationSelector);

    /// <summary>The open dialog, if any.</summary>
    public ILocator Dialog => _page.Locator(DialogSelector);

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
    /// Searches for users using the search input.
    /// </summary>
    public async Task SearchUsersAsync(string searchText)
    {
        var searchInput = _page.Locator(SearchInput);
        await searchInput.ClearAsync();
        await searchInput.FillAsync(searchText);
        await Expect(searchInput).ToHaveValueAsync(searchText);
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
        await Expect(_page.Locator(SearchInput)).ToHaveValueAsync("");
    }

    /// <summary>
    /// Asserts the current table's pager reports exactly <paramref name="expectedTotal"/> users
    /// (the "of N" part of "1-25 of N"). Retries until the server-side data lands.
    /// </summary>
    public async Task ExpectTotalUserCountAsync(int expectedTotal, int timeoutMs = 5000)
    {
        await Expect(PagerInformation).ToHaveTextAsync(
            new Regex($@"of\s+{expectedTotal}\s*$"),
            new() { Timeout = timeoutMs });
    }

    /// <summary>
    /// Asserts the current table holds at least <paramref name="minimum"/> users by waiting for the
    /// <paramref name="minimum"/>-th row. Valid while <paramref name="minimum"/> is within the first
    /// page (smallest page size is 25), where rows shown equal min(total, page size).
    /// </summary>
    public async Task ExpectTotalUserCountAtLeastAsync(int minimum, int timeoutMs = 5000)
    {
        await Expect(UserRows.Nth(minimum - 1)).ToBeVisibleAsync(new() { Timeout = timeoutMs });
    }

    /// <summary>
    /// Reads the total user count from the current table's pager ("1-25 of 42" → 42), for tests
    /// that need the value to compare against later. The caller must first sync on the table's data
    /// having loaded (e.g. a row being visible); this only waits for the pager text to be well-formed.
    /// </summary>
    public async Task<int> GetTotalUserCountAsync()
    {
        await Expect(PagerInformation).ToHaveTextAsync(new Regex(@"of\s+\d+\s*$"));

#pragma warning disable RS0030 // Value is read to compare against a later count, not asserted directly
        var text = await PagerInformation.TextContentAsync();
#pragma warning restore RS0030

        var match = Regex.Match(text ?? string.Empty, @"of\s+(\d+)\s*$");
        return int.Parse(match.Groups[1].Value);
    }

    /// <summary>
    /// Clicks the View Details button for a user.
    /// </summary>
    public async Task ClickViewDetailsAsync(string displayName)
    {
        var viewButton = UserRow(displayName).Locator($"{ActionsCell} button").First;
        await viewButton.ClickAsync();
        await Expect(Dialog).ToBeVisibleAsync();
    }

    /// <summary>
    /// Closes any open dialog.
    /// </summary>
    public async Task CloseDialogAsync()
    {
        var closeButton = Dialog.Locator("button:has-text('Close')");

#pragma warning disable RS0030 // Optional UI: not every dialog has a Close button; fall back to Escape
        var hasCloseButton = await closeButton.IsVisibleAsync();
#pragma warning restore RS0030

        if (hasCloseButton)
        {
            await closeButton.ClickAsync();
        }
        else
        {
            await _page.Keyboard.PressAsync("Escape");
        }

        // Wait for dialog to close
        await Expect(Dialog).Not.ToBeVisibleAsync();
    }

    /// <summary>
    /// Gets the current URL.
    /// </summary>
    public string CurrentUrl => _page.Url;
}
