using System.Text.RegularExpressions;
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
    private const string SidebarTitleSelector = ".sidebar-title";
    private const string SidebarSearch = ".sidebar-search";
    private const string EmptyStateSelector = ".empty-state";
    private const string EmptyStateTextSelector = ".empty-state-text";

    // Chat list selectors
    private const string ChatListEmpty = ".chat-list-empty";
    private const string ChatListItem = ".chat-list-item";
    private const string ChatTitle = ".chat-title";
    private const string ChatLastMessage = ".chat-last-message";

    // Chat header selectors
    private const string ChatHeaderSelector = ".chat-header";
    private const string ChatHeaderTitle = ".chat-header-title";
    private const string BackButton = ".back-button";

    // Messages container
    private const string MessagesContainerSelector = ".messages-container";
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
        await _page.GotoAsync("/messages");
        await _page.WaitForInteractiveAsync();
        await Expect(Layout).ToBeVisibleAsync();
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

        await _page.GotoAsync(url);
        await _page.WaitForInteractiveAsync();
        await Expect(Layout).ToBeVisibleAsync();
    }

    /// <summary>
    /// Waits for the page to fully load.
    /// </summary>
    public async Task WaitForLoadAsync(int timeoutMs = 15000)
    {
        // Wait for the layout to be present
        await Layout.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = timeoutMs
        });
    }

    /// <summary>The Telegram-style page layout.</summary>
    public ILocator Layout => _page.Locator(TelegramLayout);

    /// <summary>The chat sidebar.</summary>
    public ILocator Sidebar => _page.Locator(ChatSidebar);

    /// <summary>The sidebar title ("Chats").</summary>
    public ILocator SidebarTitle => _page.Locator(SidebarTitleSelector);

    /// <summary>The chat entries listed in the sidebar.</summary>
    public ILocator ChatItems => _page.Locator(ChatListItem);

    /// <summary>The title of every chat entry in the sidebar.</summary>
    public ILocator ChatTitles => _page.Locator($"{ChatListItem} {ChatTitle}");

    /// <summary>The sidebar chat title whose text is exactly <paramref name="chatName"/>.</summary>
    public ILocator ChatTitleNamed(string chatName) =>
        ChatTitles.Filter(new() { HasTextRegex = new Regex($@"^\s*{Regex.Escape(chatName)}\s*$") });

    /// <summary>The "no chats available" empty state in the sidebar.</summary>
    public ILocator NoChatsSidebar => _page.Locator(ChatListEmpty);

    /// <summary>
    /// Clicks on a chat by its name.
    /// </summary>
    public async Task SelectChatByNameAsync(string chatName)
    {
        var chatItem = _page.Locator(ChatListItem).Filter(new() { HasText = chatName });
        await chatItem.ClickAsync();
        await Expect(ActiveChatView).ToBeVisibleAsync();
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

    /// <summary>The empty state shown in the main view when no chat is selected.</summary>
    public ILocator EmptyState => _page.Locator($"{MainView} {EmptyStateSelector}");

    /// <summary>The text of the main-view empty state.</summary>
    public ILocator EmptyStateText => _page.Locator($"{MainView} {EmptyStateTextSelector}");

    /// <summary>The main chat view in its active state (a chat is selected).</summary>
    public ILocator ActiveChatView => _page.Locator($"{MainView}.active");

    /// <summary>
    /// Waits for the chat view to become active after selecting a chat.
    /// Uses Playwright's auto-waiting to handle Blazor re-render timing.
    /// </summary>
    public async Task WaitForChatViewActiveAsync()
    {
        await Expect(ActiveChatView).ToBeVisibleAsync();
    }

    /// <summary>The chat header of the selected chat.</summary>
    public ILocator ChatHeader => _page.Locator(ChatHeaderSelector);

    /// <summary>The selected chat's title in the chat header.</summary>
    public ILocator SelectedChatTitle => _page.Locator(ChatHeaderTitle);

    /// <summary>
    /// Clicks the back button to return to chat list.
    /// </summary>
    public async Task ClickBackButtonAsync()
    {
        await _page.Locator(BackButton).ClickAsync();
    }

    /// <summary>The messages container of the selected chat.</summary>
    public ILocator MessagesContainer => _page.Locator(MessagesContainerSelector);

    /// <summary>
    /// Gets a locator for message bubbles (for use with Expect assertions).
    /// </summary>
    public ILocator MessageBubbles => _page.Locator(MessageBubble);

    /// <summary>The "no messages" empty state within the messages container.</summary>
    public ILocator NoMessagesState => _page.Locator($"{MessagesContainerSelector} {EmptyStateSelector}");

    #region User Detail Dialog Methods

    /// <summary>
    /// The user detail dialog, located by its semantic ARIA role.
    /// More resilient to UI framework changes than CSS class selectors.
    /// </summary>
    public ILocator UserDetailDialog => _page.GetByRole(AriaRole.Dialog);

    /// <summary>The user detail dialog title.</summary>
    public ILocator UserDetailDialogTitle => UserDetailDialog.Locator(".mud-dialog-title");

    /// <summary>
    /// Clicks on a username in a message bubble to open the user detail dialog.
    /// </summary>
    public async Task ClickUsernameInMessageAsync()
    {
        var userName = _page.Locator(".tg-user-name").First;
        await userName.ClickAsync();
    }

    /// <summary>
    /// Waits for the user detail dialog to be visible.
    /// </summary>
    public async Task WaitForUserDetailDialogAsync(int timeoutMs = 5000)
    {
        await UserDetailDialog.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = timeoutMs
        });
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
        await UserDetailDialog.GetByLabel("Close").ClickAsync();
    }

    /// <summary>
    /// Waits for the user detail dialog to be hidden.
    /// </summary>
    public async Task WaitForUserDetailDialogHiddenAsync(int timeoutMs = 5000)
    {
        await UserDetailDialog.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = timeoutMs
        });
    }

    #endregion
}
