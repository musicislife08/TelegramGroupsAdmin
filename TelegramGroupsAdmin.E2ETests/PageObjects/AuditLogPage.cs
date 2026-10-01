using System.Text.RegularExpressions;
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
    private const string PageTitleSelector = ".mud-typography-h4";
    private const string TabContainer = ".mud-tabs";

    // Tables
    private const string TableRow = ".mud-table-body tr";
    private const string TablePager = ".mud-table-pagination";

    // Filter elements

    // Active tab panel scope
    private const string ActivePanel = ".mud-tab-panel:not([hidden])";

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
        // Audit log has interactive data loading - need the Blazor circuit live
        await _page.WaitForInteractiveAsync();
    }

    /// <summary>
    /// Navigates to the audit log page with a specific tab fragment.
    /// </summary>
    public async Task NavigateToTabAsync(string tabFragment)
    {
        await _page.GotoAsync($"{BasePath}#{tabFragment}");
        await _page.WaitForInteractiveAsync();
    }

    /// <summary>
    /// Waits for the page to fully load.
    /// </summary>
    public async Task WaitForLoadAsync()
    {
        await _page.WaitForInteractiveAsync();
    }

    #endregion

    #region Page Title

    /// <summary>The page title.</summary>
    public ILocator PageTitle => _page.Locator(PageTitleSelector);

    #endregion

    #region Tabs

    /// <summary>The tabs container.</summary>
    public ILocator TabsContainer => _page.Locator(TabContainer);

    /// <summary>
    /// The tab (role="tab") with the given name. MudBlazor marks the active tab with aria-selected="true".
    /// </summary>
    public ILocator Tab(string tabName) => _page.GetByRole(AriaRole.Tab, new() { Name = tabName });

    /// <summary>
    /// Clicks a tab by its text content and waits for it to become active.
    /// MudBlazor tabs use role="tab" for accessibility and aria-selected for state.
    /// </summary>
    public async Task SelectTabAsync(string tabName)
    {
        var tab = Tab(tabName);
        await tab.ClickAsync();

        // Wait for the tab to actually become active (aria-selected="true")
        // MudBlazor updates this attribute asynchronously after the click
        await Expect(tab).ToHaveAttributeAsync("aria-selected", "true", new() { Timeout = 10000 });

        await WaitForLoadAsync();
    }

    /// <summary>The Web Admin Log tab.</summary>
    public ILocator WebAdminLogTab => Tab("Web Admin Log");

    /// <summary>The Telegram Moderation Log tab.</summary>
    public ILocator ModerationLogTab => Tab("Telegram Moderation Log");

    #endregion

    #region Web Admin Log Tab

    /// <summary>The rows in the currently visible table.</summary>
    public ILocator TableRows => _page.Locator($"{ActivePanel} {TableRow}");

    /// <summary>The column header cells of the currently visible table.</summary>
    public ILocator TableHeaders => _page.Locator($"{ActivePanel} .mud-table-head th");

    /// <summary>
    /// The column header cell of the currently visible table whose text is exactly <paramref name="headerText"/>.
    /// </summary>
    public ILocator TableHeader(string headerText) =>
        TableHeaders.Filter(new() { HasTextRegex = new Regex($@"^\s*{Regex.Escape(headerText)}\s*$") });

    /// <summary>
    /// The Event Type filter select.
    /// MudBlazor renders labels inside .mud-select elements.
    /// </summary>
    public ILocator EventTypeFilter => _page.Locator(".mud-select").Filter(new() { HasText = "Event Type" }).First;

    /// <summary>The Actor filter select.</summary>
    public ILocator ActorFilter => _page.Locator(".mud-select").Filter(new() { HasText = "Actor (Who)" }).First;

    /// <summary>The Target User filter select.</summary>
    public ILocator TargetUserFilter => _page.Locator(".mud-select").Filter(new() { HasText = "Target User" }).First;

    /// <summary>
    /// Selects an event type filter option.
    /// Uses MudBlazor pattern: .mud-select with label text.
    /// </summary>
    public async Task SelectEventTypeFilterAsync(string eventType)
    {
        await EventTypeFilter.ClickAsync();

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
        await ActorFilter.ClickAsync();

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
    /// The event type chip of the log entry whose event type contains <paramref name="eventTypeText"/>.
    /// Tests should create deterministic data so only one entry matches.
    /// </summary>
    public ILocator LogEntryWithEventType(string eventTypeText) =>
        _page.Locator($"{ActivePanel} td[data-label='Event Type'] .mud-chip").Filter(new() { HasText = eventTypeText });

    /// <summary>
    /// The Actor cell of the log entry whose actor contains <paramref name="actorText"/>.
    /// Tests should create deterministic data so only one entry matches.
    /// </summary>
    public ILocator LogEntryWithActor(string actorText) =>
        _page.Locator($"{ActivePanel} td[data-label='Actor']").Filter(new() { HasText = actorText });

    #endregion

    #region Telegram Moderation Log Tab

    /// <summary>The Action Type filter select (Moderation Log tab).</summary>
    public ILocator ActionTypeFilter => _page.Locator(".mud-select").Filter(new() { HasText = "Action Type" }).First;

    /// <summary>
    /// The Telegram User ID filter (Moderation Log tab).
    /// MudTextField uses GetByPlaceholder since the label may not be accessible.
    /// </summary>
    public ILocator TelegramUserIdFilter => _page.GetByPlaceholder("Enter Telegram ID");

    /// <summary>The Issued By filter (Moderation Log tab).</summary>
    public ILocator IssuedByFilter => _page.GetByPlaceholder("e.g. Exam Flow or an admin's email");

    /// <summary>
    /// Selects an action type filter option.
    /// </summary>
    public async Task SelectActionTypeFilterAsync(string actionType)
    {
        await ActionTypeFilter.ClickAsync();

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
    /// Fills the Telegram User ID filter and commits it. The field is a non-Immediate MudTextField,
    /// so typing alone never reaches <c>ValueChanged</c>: the value only commits on the input's
    /// change event, which Enter raises. The table reload is awaited by the caller's Expects on the
    /// table (nothing on the field itself distinguishes a committed value from a typed one).
    /// </summary>
    public async Task FilterByTelegramUserIdAsync(string userId)
    {
        await CommitFilterAsync(TelegramUserIdFilter, userId);
    }

    /// <summary>
    /// Fills the Issued By filter and commits it (see <see cref="FilterByTelegramUserIdAsync"/>).
    /// </summary>
    public async Task FilterByIssuedByAsync(string issuedBy)
    {
        await CommitFilterAsync(IssuedByFilter, issuedBy);
    }

    private static async Task CommitFilterAsync(ILocator field, string value)
    {
        await field.FillAsync(value);
        await field.PressAsync("Enter");
    }

    /// <summary>The Telegram User cells of the moderation table's current page.</summary>
    public ILocator ModerationTelegramUserCells => _page.Locator($"{ActivePanel} td[data-label='Telegram User']");

    /// <summary>The Issued By cells of the moderation table's current page.</summary>
    public ILocator ModerationIssuedByCells => _page.Locator($"{ActivePanel} td[data-label='Issued By']");

    /// <summary>
    /// The Telegram User cells whose user id is exactly <paramref name="userId"/>: a known user renders
    /// its display name over an "ID: 123" caption, an unknown one renders the bare id. Lookarounds
    /// stand in for <c>\b</c>, which never matches in Playwright's regex bridge.
    /// </summary>
    public ILocator ModerationEntriesForTelegramUser(long userId) =>
        ModerationTelegramUserCells.Filter(new() { HasTextRegex = new Regex($@"(?<!\d){userId}(?!\d)") });

    /// <summary>
    /// Clears the Action Type filter by selecting "All Actions".
    /// </summary>
    public async Task ClearActionTypeFilterAsync()
    {
        await SelectActionTypeFilterAsync("All Actions");
    }

    /// <summary>
    /// The action type chip of the moderation entry whose action type contains <paramref name="actionTypeText"/>.
    /// </summary>
    public ILocator ModerationEntryWithActionType(string actionTypeText) =>
        _page.Locator($"{ActivePanel} td[data-label='Action Type'] .mud-chip").Filter(new() { HasText = actionTypeText });

    /// <summary>
    /// The Issued By cell of the moderation entry whose issuer contains <paramref name="issuedByText"/>.
    /// </summary>
    public ILocator ModerationEntryWithIssuedBy(string issuedByText) =>
        _page.Locator($"{ActivePanel} td[data-label='Issued By']").Filter(new() { HasText = issuedByText });

    #endregion

    #region Table Pagination

    /// <summary>The table pager of the currently visible table.</summary>
    public ILocator Pager => _page.Locator($"{ActivePanel} {TablePager}");

    /// <summary>The pager's "1-10 of N" text of the currently visible table.</summary>
    public ILocator PagerInformation => Pager.Locator(".mud-table-page-number-information");

    /// <summary>
    /// Asserts the visible table's pager reports exactly <paramref name="expectedTotal"/> rows in total
    /// (the "of N" part of "1-10 of N"), retrying until the server-side data lands.
    /// </summary>
    public Task ExpectTotalRowCountAsync(int expectedTotal) =>
        Expect(PagerInformation).ToHaveTextAsync(new Regex($@"of\s+{expectedTotal}\s*$"));

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

    #endregion
}
