using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.PageObjects;

/// <summary>
/// Page object for Messages.razor (/messages - the message history view).
/// Implements Telegram Desktop-style layout with chat sidebar and message view.
/// </summary>
public class MessagesPage
{
    private readonly IPage _page;

    // Selectors - Layout structure
    private const string TelegramLayout = ".telegram-layout";
    private const string ChatSidebar = ".telegram-sidebar";
    private const string MainView = ".telegram-main";
    private const string SidebarTitle = ".sidebar-title";
    private const string SidebarSearch = ".sidebar-search";
    private const string EmptyState = ".empty-state";
    private const string EmptyStateText = ".empty-state-text";
    private const string LoadingIndicator = ".mud-progress-circular";

    // Chat list selectors
    private const string ChatList = ".chat-list";
    private const string ChatListEmpty = ".chat-list-empty";
    private const string ChatListItem = ".chat-list-item";
    private const string ChatTitle = ".chat-title";
    private const string ChatLastMessage = ".chat-last-message";

    // Chat header selectors
    private const string ChatHeader = ".chat-header";
    private const string ChatHeaderTitle = ".chat-header-title";
    private const string BackButton = ".back-button";

    // Messages container
    private const string MessagesContainer = ".messages-container";
    private const string MessageBubble = ".tg-message"; // Telegram-style message bubble

    public MessagesPage(IPage page)
    {
        _page = page;
    }

    /// <summary>
    /// Navigates to the messages page.
    /// </summary>
    public async Task NavigateAsync()
    {
        await _page.GotoAsync("/messages", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await Expect(_page.Locator(TelegramLayout)).ToBeVisibleAsync();
    }

    /// <summary>
    /// Navigates to the messages page with query parameters.
    /// </summary>
    public async Task NavigateAsync(long? chatId = null, long? highlightMessageId = null)
    {
        var url = "/messages";
        var queryParams = new List<string>();

        if (chatId.HasValue)
            queryParams.Add($"chat={chatId.Value}");

        if (highlightMessageId.HasValue)
            queryParams.Add($"highlight={highlightMessageId.Value}");

        if (queryParams.Count > 0)
            url += "?" + string.Join("&", queryParams);

        await _page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await Expect(_page.Locator(TelegramLayout)).ToBeVisibleAsync();
    }

    /// <summary>
    /// Waits for the page to fully load.
    /// </summary>
    public async Task WaitForLoadAsync(int timeoutMs = 15000)
    {
        // Wait for the layout to be present
        await _page.Locator(TelegramLayout).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = timeoutMs
        });
    }

    /// <summary>
    /// Checks if the page layout is visible.
    /// </summary>
    public async Task<bool> IsLayoutVisibleAsync()
    {
        return await _page.Locator(TelegramLayout).IsVisibleAsync();
    }

    /// <summary>
    /// Checks if the chat sidebar is visible.
    /// </summary>
    public async Task<bool> IsSidebarVisibleAsync()
    {
        return await _page.Locator(ChatSidebar).IsVisibleAsync();
    }

    /// <summary>
    /// Gets the sidebar title text.
    /// </summary>
    public async Task<string?> GetSidebarTitleAsync()
    {
        return await _page.Locator(SidebarTitle).TextContentAsync();
    }

    /// <summary>
    /// Gets the count of chats displayed in the sidebar.
    /// </summary>
    public async Task<int> GetChatCountAsync()
    {
        return await _page.Locator(ChatListItem).CountAsync();
    }

    /// <summary>
    /// Gets the names of all chats in the sidebar.
    /// </summary>
    public async Task<List<string>> GetChatNamesAsync()
    {
        var chatNames = new List<string>();
        var items = await _page.Locator($"{ChatListItem} {ChatTitle}").AllAsync();

        foreach (var item in items)
        {
            var text = await item.TextContentAsync();
            if (!string.IsNullOrEmpty(text))
                chatNames.Add(text);
        }

        return chatNames;
    }

    /// <summary>
    /// Checks if the "no chats available" empty state is visible in the sidebar.
    /// </summary>
    public async Task<bool> IsNoChatsSidebarVisibleAsync()
    {
        return await _page.Locator(ChatListEmpty).IsVisibleAsync();
    }

    /// <summary>
    /// Clicks on a chat by its name.
    /// </summary>
    public async Task SelectChatByNameAsync(string chatName)
    {
        var chatItem = _page.Locator(ChatListItem).Filter(new() { HasText = chatName });
        await chatItem.ClickAsync();
        await Expect(_page.Locator($"{MainView}.active")).ToBeVisibleAsync();
    }

    /// <summary>
    /// Searches for chats using the sidebar search.
    /// The input uses @oninput to trigger filtering on each keystroke.
    /// </summary>
    public async Task SearchChatsAsync(string searchText)
    {
        var searchInput = _page.Locator(SidebarSearch);
        await searchInput.ClearAsync();
        // Type character by character to trigger oninput event
        await searchInput.PressSequentiallyAsync(searchText, new LocatorPressSequentiallyOptions { Delay = 50 });
        await Expect(searchInput).ToHaveValueAsync(searchText);
    }

    /// <summary>
    /// Clears the chat search input.
    /// </summary>
    public async Task ClearChatSearchAsync()
    {
        var searchInput = _page.Locator(SidebarSearch);
        // Clear and dispatch input event to trigger filtering
        await searchInput.FillAsync("");
        await searchInput.DispatchEventAsync("input");
        await Expect(searchInput).ToHaveValueAsync("");
    }

    /// <summary>
    /// Checks if the empty state (no chat selected) is visible.
    /// </summary>
    public async Task<bool> IsEmptyStateVisibleAsync()
    {
        return await _page.Locator($"{MainView} {EmptyState}").IsVisibleAsync();
    }

    /// <summary>
    /// Gets the empty state text.
    /// </summary>
    public async Task<string?> GetEmptyStateTextAsync()
    {
        return await _page.Locator($"{MainView} {EmptyStateText}").TextContentAsync();
    }

    /// <summary>
    /// Checks if the main chat view is active (a chat is selected).
    /// </summary>
    public async Task<bool> IsChatViewActiveAsync()
    {
        return await _page.Locator($"{MainView}.active").IsVisibleAsync();
    }

    /// <summary>
    /// Waits for the chat view to become active after selecting a chat.
    /// Uses Playwright's auto-waiting to handle Blazor re-render timing.
    /// </summary>
    public async Task WaitForChatViewActiveAsync()
    {
        await Expect(_page.Locator($"{MainView}.active")).ToBeVisibleAsync();
    }

    /// <summary>
    /// Checks if the chat header is visible.
    /// </summary>
    public async Task<bool> IsChatHeaderVisibleAsync()
    {
        return await _page.Locator(ChatHeader).IsVisibleAsync();
    }

    /// <summary>
    /// Gets the selected chat title from the header.
    /// </summary>
    public async Task<string?> GetSelectedChatTitleAsync()
    {
        return await _page.Locator(ChatHeaderTitle).TextContentAsync();
    }

    /// <summary>
    /// Clicks the back button to return to chat list.
    /// </summary>
    public async Task ClickBackButtonAsync()
    {
        await _page.Locator(BackButton).ClickAsync();
    }

    /// <summary>
    /// Checks if the messages container is visible.
    /// </summary>
    public async Task<bool> IsMessagesContainerVisibleAsync()
    {
        return await _page.Locator(MessagesContainer).IsVisibleAsync();
    }

    /// <summary>
    /// Gets the count of messages displayed.
    /// </summary>
    public async Task<int> GetMessageCountAsync()
    {
        return await _page.Locator(MessageBubble).CountAsync();
    }

    /// <summary>
    /// Checks if loading indicator is visible.
    /// </summary>
    public async Task<bool> IsLoadingAsync()
    {
        return await _page.Locator($"{MessagesContainer} {LoadingIndicator}").IsVisibleAsync();
    }

    /// <summary>
    /// Waits for messages to load (loading indicator disappears).
    /// </summary>
    public async Task WaitForMessagesLoadAsync(int timeoutMs = 10000)
    {
        // Wait for loading to disappear
        await _page.Locator($"{MessagesContainer} {LoadingIndicator}").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = timeoutMs
        });
    }

    /// <summary>
    /// Gets a locator for message bubbles (for use with Expect assertions).
    /// </summary>
    public ILocator MessageBubbles => _page.Locator(MessageBubble);

    /// <summary>
    /// Checks if the "no messages" empty state is visible (within messages container).
    /// </summary>
    public async Task<bool> IsNoMessagesStateVisibleAsync()
    {
        return await _page.Locator($"{MessagesContainer} {EmptyState}").IsVisibleAsync();
    }

    /// <summary>
    /// Gets the current URL.
    /// </summary>
    public string CurrentUrl => _page.Url;

    #region User Detail Dialog Methods

    /// <summary>
    /// Gets a locator for the dialog using semantic ARIA role.
    /// More resilient to UI framework changes than CSS class selectors.
    /// </summary>
    private ILocator DialogLocator => _page.GetByRole(AriaRole.Dialog);

    /// <summary>
    /// Clicks on a username in a message bubble to open the user detail dialog.
    /// </summary>
    public async Task ClickUsernameInMessageAsync()
    {
        var userName = _page.Locator(".tg-user-name").First;
        await userName.ClickAsync();
    }

    /// <summary>
    /// Checks if the user detail dialog is visible.
    /// </summary>
    public async Task<bool> IsUserDetailDialogVisibleAsync()
    {
        return await DialogLocator.IsVisibleAsync();
    }

    /// <summary>
    /// Waits for the user detail dialog to be visible.
    /// </summary>
    public async Task WaitForUserDetailDialogAsync(int timeoutMs = 5000)
    {
        await DialogLocator.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = timeoutMs
        });
    }

    /// <summary>
    /// Gets the user detail dialog title text.
    /// </summary>
    public async Task<string?> GetUserDetailDialogTitleAsync()
    {
        // Title is within the dialog - scope the search
        return await DialogLocator.Locator(".mud-dialog-title").TextContentAsync();
    }

    /// <summary>
    /// Gets the content text of the user detail dialog.
    /// Uses InnerTextAsync for better text extraction from MudBlazor components.
    /// </summary>
    public async Task<string?> GetUserDetailDialogContentAsync()
    {
        // Get all visible text from the dialog using InnerTextAsync
        // (TextContentAsync may return empty for complex MudBlazor component trees)
        return await DialogLocator.InnerTextAsync();
    }

    /// <summary>
    /// Closes the user detail dialog by pressing Escape.
    /// </summary>
    public async Task CloseUserDetailDialogByEscapeAsync()
    {
        await _page.Keyboard.PressAsync("Escape");
    }

    /// <summary>
    /// Closes the user detail dialog by clicking the close button.
    /// </summary>
    public async Task CloseUserDetailDialogByButtonAsync()
    {
        // Use GetByLabel to target the icon button with aria-label="Close"
        // (avoids ambiguity with any button that has "Close" text)
        await DialogLocator.GetByLabel("Close").ClickAsync();
    }

    /// <summary>
    /// Waits for the user detail dialog to be hidden.
    /// </summary>
    public async Task WaitForUserDetailDialogHiddenAsync(int timeoutMs = 5000)
    {
        await DialogLocator.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = timeoutMs
        });
    }

    #endregion
}
