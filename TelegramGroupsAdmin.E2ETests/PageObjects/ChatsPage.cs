using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.PageObjects;

/// <summary>
/// Page object for Chats.razor (/chats - the chat management page).
/// Provides methods to interact with the MudTable displaying managed chats.
/// </summary>
public class ChatsPage
{
    private readonly IPage _page;

    // Selectors - Layout
    private const string PageTitleSelector = ".mud-typography-h4";
    private const string LoadingIndicator = ".mud-progress-linear";
    private const string EmptyAlertSelector = ".mud-alert";
    private const string EmptyAlertTitleSelector = ".mud-alert .mud-typography-h6";

    // Selectors - MudTable
    private const string ChatsTableSelector = ".mud-table";
    private const string TableTitleSelector = ".mud-table-toolbar .mud-typography-h6";
    private const string SearchInput = ".mud-table-toolbar .mud-input input";
    private const string TableRow = ".mud-table-body tr";
    private const string TablePager = ".mud-table-pagination";

    // Selectors - Row content
    private const string ChatNameCell = "td[data-label='Chat Name']";
    private const string ChatTypeCell = "td[data-label='Type']";
    private const string BotStatusCell = "td[data-label='Bot Status']";
    private const string HealthCell = "td[data-label='Health']";
    private const string CustomConfigCell = "td[data-label='Custom Config']";
    private const string ConfigureButton = "button:has-text('Configure')";

    public ChatsPage(IPage page)
    {
        _page = page;
    }

    /// <summary>
    /// Navigates to the chats page.
    /// </summary>
    public async Task NavigateAsync()
    {
        await _page.GotoAsync("/chats");
        await _page.WaitForInteractiveAsync();
        await Expect(PageTitle).ToBeVisibleAsync();
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

    /// <summary>The page title.</summary>
    public ILocator PageTitle => _page.Locator(PageTitleSelector);

    /// <summary>The empty state alert.</summary>
    public ILocator EmptyStateAlert => _page.Locator(EmptyAlertSelector);

    /// <summary>The empty state alert title.</summary>
    public ILocator EmptyStateTitle => _page.Locator(EmptyAlertTitleSelector);

    /// <summary>The chats table.</summary>
    public ILocator ChatsTable => _page.Locator(ChatsTableSelector);

    /// <summary>The table title.</summary>
    public ILocator TableTitle => _page.Locator(TableTitleSelector);

    /// <summary>
    /// Searches for chats using the search input.
    /// MudTextField uses Immediate="true" so it filters on each keystroke.
    /// </summary>
    public async Task SearchChatsAsync(string searchText)
    {
        var searchInput = _page.Locator(SearchInput);
        await searchInput.ClearAsync();
        await searchInput.FillAsync(searchText);

        // MudTextField with Immediate="true" triggers Blazor's async re-render
        // Client-side filtering doesn't make network requests, so NetworkIdle is insufficient
        // Wait for input to have the expected value (confirms input was processed)
        await Expect(searchInput).ToHaveValueAsync(searchText);
    }

    /// <summary>
    /// Waits for the visible chat count to equal the expected value using Playwright's auto-retry.
    /// MudTable filtering may leave hidden rows in the DOM - only visible ones are counted.
    /// </summary>
    public async Task ExpectChatCountAsync(int expectedCount, int timeoutMs = 5000)
    {
        // Use Playwright's auto-retrying WaitForFunction to check visible row count
        // This properly waits for Blazor to complete re-rendering after filter
        await _page.WaitForFunctionAsync(
            @"([selector, expected]) => {
                const rows = document.querySelectorAll(selector);
                let visibleCount = 0;
                for (const row of rows) {
                    // offsetParent is null for hidden elements (display:none or not in DOM tree)
                    if (row.offsetParent !== null) visibleCount++;
                }
                return visibleCount === expected;
            }",
            new object[] { TableRow, expectedCount },
            new PageWaitForFunctionOptions { Timeout = timeoutMs, PollingInterval = 100 });
    }

    /// <summary>
    /// Clears the search input.
    /// </summary>
    public async Task ClearSearchAsync()
    {
        var searchInput = _page.Locator(SearchInput);
        await searchInput.ClearAsync();

        // Wait for input to be empty (confirms clear was processed)
        await Expect(searchInput).ToHaveValueAsync("");
    }

    /// <summary>
    /// The chat name text of the table row whose chat name is exactly <paramref name="chatName"/>.
    /// </summary>
    public ILocator ChatName(string chatName) =>
        _page.Locator($"{TableRow} {ChatNameCell} .mud-typography-body2")
            .Filter(new() { HasTextRegex = new Regex($@"^\s*{Regex.Escape(chatName)}\s*$") });

    /// <summary>The table row containing <paramref name="chatName"/>.</summary>
    public ILocator ChatRow(string chatName) => _page.Locator(TableRow).Filter(new() { HasText = chatName });

    /// <summary>The chat type cell for a chat by its name.</summary>
    public ILocator ChatTypeCellFor(string chatName) => ChatRow(chatName).Locator(ChatTypeCell);

    /// <summary>The bot status chip for a chat by its name.</summary>
    public ILocator BotStatusChip(string chatName) => ChatRow(chatName).Locator($"{BotStatusCell} .mud-chip");

    /// <summary>The health status chip for a chat by its name.</summary>
    public ILocator HealthStatusChip(string chatName) => ChatRow(chatName).Locator($"{HealthCell} .mud-chip");

    /// <summary>
    /// The custom config indicator (check icon) for a chat by its name.
    /// Absent when the chat uses the global config (the cell shows "Global" instead).
    /// </summary>
    public ILocator CustomConfigIcon(string chatName) => ChatRow(chatName).Locator($"{CustomConfigCell} .mud-icon-root");

    /// <summary>The Configure button in the row of the chat named <paramref name="chatName"/>.</summary>
    public ILocator ConfigureButtonFor(string chatName) => ChatRow(chatName).Locator(ConfigureButton);

    /// <summary>
    /// Clicks the Configure button for a chat by its name.
    /// </summary>
    public async Task ClickConfigureAsync(string chatName)
    {
        await ConfigureButtonFor(chatName).ClickAsync();
        await Expect(_page.GetByRole(AriaRole.Dialog)).ToBeVisibleAsync();
    }

    /// <summary>
    /// Ticks the "Show deleted chats" checkbox (MudCheckBox: the label text is the click target).
    /// Callers sync on a deleted chat's row appearing.
    /// </summary>
    public async Task ShowDeletedChatsAsync()
    {
        // A click on the prerendered checkbox has no handler; wait for the live circuit first
        await _page.WaitForInteractiveAsync();
        await _page.GetByText("Show deleted chats").ClickAsync();
    }

    /// <summary>The "Inactive" chip for a chat by its name.</summary>
    public ILocator InactiveChip(string chatName) => ChatRow(chatName).Locator(".mud-chip:has-text('Inactive')");

    /// <summary>The table pager.</summary>
    public ILocator Pager => _page.Locator(TablePager);

    /// <summary>
    /// The open dialog. MudBlazor dialogs render with .mud-dialog-container containing .mud-dialog element;
    /// the semantic role locator is used for better reliability.
    /// </summary>
    public ILocator Dialog => _page.GetByRole(AriaRole.Dialog);

    /// <summary>
    /// The dialog title. The dialog title is typically in a header element or the first text.
    /// </summary>
    public ILocator DialogTitle => _page.Locator(".mud-dialog-container .mud-typography-h6").First;

    /// <summary>
    /// Closes the dialog by clicking the close button or pressing Escape.
    /// </summary>
    public async Task CloseDialogAsync()
    {
        var dialog = _page.GetByRole(AriaRole.Dialog);
        var closeButton = dialog.Locator("button:has-text('Close')");

        // Genuinely optional UI: some dialogs have no Close button and are dismissed with Escape instead.
        // The dialog itself is already open (ClickConfigureAsync waits for it) before this branch runs.
#pragma warning disable RS0030 // Branching on optional UI (Close button may not exist), not an assertion
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
        await Expect(dialog).Not.ToBeVisibleAsync();
    }

    /// <summary>
    /// Gets the current URL.
    /// </summary>
    public string CurrentUrl => _page.Url;
}
