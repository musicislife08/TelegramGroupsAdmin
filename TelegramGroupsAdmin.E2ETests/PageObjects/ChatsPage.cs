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
    private const string PageTitle = ".mud-typography-h4";
    private const string LoadingIndicator = ".mud-progress-linear";
    private const string EmptyAlert = ".mud-alert";
    private const string EmptyAlertTitle = ".mud-alert .mud-typography-h6";

    // Selectors - MudTable
    private const string ChatsTable = ".mud-table";
    private const string TableToolbar = ".mud-table-toolbar";
    private const string TableTitle = ".mud-table-toolbar .mud-typography-h6";
    private const string SearchInput = ".mud-table-toolbar .mud-input input";
    private const string TableBody = ".mud-table-body";
    private const string TableRow = ".mud-table-body tr";
    private const string TablePager = ".mud-table-pagination";

    // Selectors - Row content
    private const string ChatNameCell = "td[data-label='Chat Name']";
    private const string ChatTypeCell = "td[data-label='Type']";
    private const string BotStatusCell = "td[data-label='Bot Status']";
    private const string HealthCell = "td[data-label='Health']";
    private const string CustomConfigCell = "td[data-label='Custom Config']";
    private const string ActionsCell = "td[data-label='Actions']";
    private const string ConfigureButton = "button:has-text('Configure')";
    private const string RefreshButton = "button[title='Refresh health status']";

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
    /// Checks if the empty state alert is visible.
    /// </summary>
    public async Task<bool> IsEmptyStateVisibleAsync()
    {
        return await _page.Locator(EmptyAlert).IsVisibleAsync();
    }

    /// <summary>
    /// Gets the empty state alert title text.
    /// </summary>
    public async Task<string?> GetEmptyStateTitleAsync()
    {
        return await _page.Locator(EmptyAlertTitle).TextContentAsync();
    }

    /// <summary>
    /// Checks if the chats table is visible.
    /// </summary>
    public async Task<bool> IsTableVisibleAsync()
    {
        return await _page.Locator(ChatsTable).IsVisibleAsync();
    }

    /// <summary>
    /// Gets the table title text.
    /// </summary>
    public async Task<string?> GetTableTitleAsync()
    {
        return await _page.Locator(TableTitle).TextContentAsync();
    }

    /// <summary>
    /// Gets the count of rows in the table.
    /// </summary>
    public async Task<int> GetChatCountAsync()
    {
        return await _page.Locator(TableRow).CountAsync();
    }

    /// <summary>
    /// Searches for chats using the search input.
    /// MudTextField uses Immediate="true" so it filters on each keystroke.
    /// </summary>
    public async Task SearchChatsAsync(string searchText)
    {
        var searchInput = _page.Locator(SearchInput);
        await searchInput.ClearAsync();
        await searchInput.FillAsync(searchText);
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>
    /// Clears the search input.
    /// </summary>
    public async Task ClearSearchAsync()
    {
        var searchInput = _page.Locator(SearchInput);
        await searchInput.ClearAsync();
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>
    /// Gets the names of all visible chats in the table.
    /// </summary>
    public async Task<List<string>> GetChatNamesAsync()
    {
        var chatNames = new List<string>();
        var rows = await _page.Locator(TableRow).AllAsync();

        foreach (var row in rows)
        {
            var nameCell = row.Locator(ChatNameCell);
            var nameText = await nameCell.Locator(".mud-typography-body2").TextContentAsync();
            if (!string.IsNullOrEmpty(nameText))
                chatNames.Add(nameText);
        }

        return chatNames;
    }

    /// <summary>
    /// Gets the chat ID displayed for a chat by its name.
    /// </summary>
    public async Task<string?> GetChatIdByNameAsync(string chatName)
    {
        var row = _page.Locator(TableRow).Filter(new() { HasText = chatName });
        var idText = await row.Locator($"{ChatNameCell} .mud-typography-caption").TextContentAsync();
        // Extract just the ID number from "ID: 123456"
        return idText?.Replace("ID: ", "");
    }

    /// <summary>
    /// Gets the chat type for a chat by its name.
    /// </summary>
    public async Task<string?> GetChatTypeByNameAsync(string chatName)
    {
        var row = _page.Locator(TableRow).Filter(new() { HasText = chatName });
        return await row.Locator(ChatTypeCell).TextContentAsync();
    }

    /// <summary>
    /// Gets the bot status for a chat by its name.
    /// </summary>
    public async Task<string?> GetBotStatusByNameAsync(string chatName)
    {
        var row = _page.Locator(TableRow).Filter(new() { HasText = chatName });
        return await row.Locator($"{BotStatusCell} .mud-chip").TextContentAsync();
    }

    /// <summary>
    /// Gets the health status for a chat by its name.
    /// </summary>
    public async Task<string?> GetHealthStatusByNameAsync(string chatName)
    {
        var row = _page.Locator(TableRow).Filter(new() { HasText = chatName });
        return await row.Locator($"{HealthCell} .mud-chip").TextContentAsync();
    }

    /// <summary>
    /// Checks if a chat has a custom config indicator.
    /// </summary>
    public async Task<bool> HasCustomConfigAsync(string chatName)
    {
        var row = _page.Locator(TableRow).Filter(new() { HasText = chatName });
        var checkIcon = row.Locator($"{CustomConfigCell} .mud-icon-root");
        return await checkIcon.IsVisibleAsync();
    }

    /// <summary>
    /// Clicks the Configure button for a chat by its name.
    /// </summary>
    public async Task ClickConfigureAsync(string chatName)
    {
        var row = _page.Locator(TableRow).Filter(new() { HasText = chatName });
        await row.Locator(ConfigureButton).ClickAsync();
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>
    /// Clicks the Refresh health button for a chat by its name.
    /// </summary>
    public async Task ClickRefreshHealthAsync(string chatName)
    {
        var row = _page.Locator(TableRow).Filter(new() { HasText = chatName });
        await row.Locator(RefreshButton).ClickAsync();
    }

    /// <summary>
    /// Checks if the inactive chip is visible for a chat.
    /// </summary>
    public async Task<bool> HasInactiveChipAsync(string chatName)
    {
        var row = _page.Locator(TableRow).Filter(new() { HasText = chatName });
        var inactiveChip = row.Locator(".mud-chip:has-text('Inactive')");
        return await inactiveChip.IsVisibleAsync();
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
    /// MudBlazor dialogs render with .mud-dialog-container containing .mud-dialog element.
    /// </summary>
    public async Task<bool> IsDialogOpenAsync()
    {
        // MudBlazor dialogs use .mud-dialog inside .mud-dialog-container
        // The .mud-dialog element is the actual dialog content wrapper
        var mudDialog = _page.Locator(".mud-dialog");
        return await mudDialog.IsVisibleAsync();
    }

    /// <summary>
    /// Gets the dialog title.
    /// </summary>
    public async Task<string?> GetDialogTitleAsync()
    {
        // The dialog title is typically in a header element or the first text
        return await _page.Locator(".mud-dialog-container .mud-typography-h6").First.TextContentAsync();
    }

    /// <summary>
    /// Closes the dialog by clicking the close button or pressing Escape.
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
