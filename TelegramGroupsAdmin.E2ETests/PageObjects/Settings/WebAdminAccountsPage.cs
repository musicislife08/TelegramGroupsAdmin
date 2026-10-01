using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.PageObjects.Settings;

/// <summary>
/// Page object for the Web Admin Accounts settings section (/settings/system/accounts).
/// Provides methods to manage admin users, invites, and account settings.
/// </summary>
public class WebAdminAccountsPage
{
    private readonly IPage _page;

    // Selectors - Layout
    private const string PageTitleSelector = ".mud-typography-h4";
    private const string LoadingIndicator = ".mud-progress-linear";
    private const string UserTableSelector = ".mud-table";
    private const string UserTableRow = ".mud-table-body tr";

    // Selectors - Action buttons (top)
    private const string CreateUserButtonSelector = "button:has-text('Create User')";
    private const string ManageInvitesButtonSelector = "button:has-text('Manage Invites')";

    // Selectors - Status filter (the MudSelect has Label="Status Filter")

    // Selectors - Table headers
    private const string TableHeaderSelector = ".mud-table-head th";

    // Selectors - Table cells
    private const string PermissionCell = "td[data-label='Permission Level']";
    private const string TotpCell = "td[data-label='TOTP']";

    // Selectors - Action menu (the MudMenu trigger is the only button in the Actions cell;
    // MudBlazor 9.9 renders no data-testid on icons)
    private const string ActionMenuButton = "td[data-label='Actions'] button";

    // Selectors - Dialogs
    private const string DialogSelector = ".mud-dialog";
    private const string DialogTitleSelector = ".mud-dialog-title";
    private const string ConfirmButton = ".mud-dialog button:has-text('Disable'), .mud-dialog button:has-text('Delete'), .mud-dialog button:has-text('Reset'), .mud-dialog button:has-text('Unlock'), .mud-dialog button:has-text('Restore')";
    private const string CancelButton = ".mud-dialog button:has-text('Cancel')";

    // Create Invite Dialog selectors
    private const string PermissionSelect = ".mud-dialog .mud-select";
    private const string ValidDaysInput = ".mud-dialog .mud-input input[type='number']";
    private const string CreateInviteDialogButton = ".mud-dialog button:has-text('Create Invite')";
    private const string InviteLinkText = ".mud-dialog .mud-typography:has-text('register?invite=')";

    public WebAdminAccountsPage(IPage page)
    {
        _page = page;
    }

    /// <summary>
    /// Navigates to the Web Admin Accounts settings page.
    /// </summary>
    public async Task NavigateAsync()
    {
        await _page.GotoAsync("/settings/system/accounts");
        // Settings pages need the Blazor circuit live for interactions
        await _page.WaitForInteractiveAsync();
    }

    /// <summary>
    /// Waits for the page to finish loading.
    /// </summary>
    public async Task WaitForLoadAsync(int timeoutMs = 15000)
    {
        // Wait for table to be visible
        await UserTable.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = timeoutMs
        });

        // Wait for loading indicator to disappear (use .First to avoid strict mode when multiple progress bars exist)
        var loadingIndicator = _page.Locator(LoadingIndicator).First;
        await Expect(loadingIndicator).Not.ToBeVisibleAsync(new() { Timeout = 5000 });
    }

    /// <summary>The page title.</summary>
    public ILocator PageTitle => _page.Locator(PageTitleSelector);

    /// <summary>The user table.</summary>
    public ILocator UserTable => _page.Locator(UserTableSelector);

    /// <summary>
    /// The user table header whose text is exactly <paramref name="name"/> (e.g. "Email", "Status").
    /// </summary>
    public ILocator TableHeader(string name) =>
        _page.Locator(TableHeaderSelector).Filter(new() { HasTextRegex = new Regex($@"^\s*{Regex.Escape(name)}\s*$") });

    /// <summary>The user rows displayed in the table.</summary>
    public ILocator UserRows => _page.Locator(UserTableRow);

    /// <summary>The Create User button.</summary>
    public ILocator CreateUserButton => _page.Locator(CreateUserButtonSelector);

    /// <summary>The Manage Invites button.</summary>
    public ILocator ManageInvitesButton => _page.Locator(ManageInvitesButtonSelector);

    /// <summary>
    /// Clicks the Create User button to open the invite dialog.
    /// Waits for dialog to be visible before returning.
    /// </summary>
    public async Task ClickCreateUserAsync()
    {
        await CreateUserButton.ClickAsync();

        // Wait for dialog to appear using Playwright's auto-waiting
        await Dialog.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
    }

    /// <summary>
    /// Clicks the Manage Invites button to open the invites dialog.
    /// Waits for dialog to be visible before returning.
    /// </summary>
    public async Task ClickManageInvitesAsync()
    {
        await ManageInvitesButton.ClickAsync();

        // Wait for dialog to appear using Playwright's auto-waiting
        await Dialog.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
    }

    /// <summary>The currently open dialog.</summary>
    public ILocator Dialog => _page.Locator(DialogSelector);

    /// <summary>The title of the currently open dialog.</summary>
    public ILocator DialogTitle => _page.Locator(DialogTitleSelector);

    /// <summary>The open MudSelect dropdown popover.</summary>
    private ILocator OpenPopover => _page.Locator(".mud-popover-open");

    /// <summary>
    /// The permission options in the open permission dropdown of the create invite dialog.
    /// Call <see cref="OpenPermissionOptionsAsync"/> first.
    /// </summary>
    public ILocator PermissionOptions => OpenPopover.Locator(".mud-list-item");

    /// <summary>
    /// Opens the permission dropdown in the create invite dialog and waits for it to show.
    /// Scopes the search to within the dialog to avoid matching other selects on the page.
    /// </summary>
    public async Task OpenPermissionOptionsAsync()
    {
        // Find the Permission Level select within the dialog (scoped to avoid the status filter
        // select outside the dialog). MudSelect creates an input with the label, we need to click
        // on the select container
        var selectContainer = _page.Locator(PermissionSelect).First;
        await selectContainer.ClickAsync();

        // Wait for the popover to open - Playwright's auto-waiting handles this
        await OpenPopover.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
    }

    /// <summary>
    /// Closes the permission dropdown by pressing Escape and waits for the popover to close.
    /// </summary>
    public async Task ClosePermissionOptionsAsync()
    {
        await _page.Keyboard.PressAsync("Escape");
        await OpenPopover.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 2000
        });
    }

    /// <summary>
    /// Closes the current dialog and waits for it to disappear.
    /// </summary>
    public async Task CloseDialogAsync()
    {
        var dialog = Dialog;

        // Try clicking cancel, if not available press escape
        var cancelButton = _page.Locator(CancelButton);
#pragma warning disable RS0030 // Not every dialog renders a Cancel button; branch picks how to close the already-open dialog
        var hasCancelButton = await cancelButton.IsVisibleAsync();
#pragma warning restore RS0030
        if (hasCancelButton)
        {
            await cancelButton.ClickAsync();
        }
        else
        {
            await _page.Keyboard.PressAsync("Escape");
        }

        // Wait for dialog to close using Playwright's auto-waiting
        await dialog.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 5000
        });
    }

    /// <summary>
    /// Opens the action menu for a specific user and waits for menu to appear.
    /// </summary>
    public async Task OpenActionMenuForUserAsync(string email)
    {
        var menuButton = UserRow(email).Locator(ActionMenuButton);
        await menuButton.ClickAsync();

        // Wait for menu popover to appear
        var menuPopover = _page.Locator(".mud-popover-open");
        await menuPopover.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
    }

    /// <summary>The items of the open action menu.</summary>
    public ILocator ActionMenuItems => OpenPopover.Locator(".mud-menu-item");

    /// <summary>
    /// Clicks an action menu item by text. The menu closes once the item's handler returns, which for
    /// the confirming actions is only after their dialog closes — so this does not wait for the menu;
    /// <see cref="ConfirmDialogAsync"/> / <see cref="CancelDialogAsync"/> wait for it instead.
    /// </summary>
    public Task ClickActionMenuItemAsync(string itemText) =>
        ActionMenuItems.Filter(new() { HasText = itemText }).ClickAsync();

    /// <summary>
    /// Confirms the current confirmation dialog and waits for it to close.
    /// </summary>
    public async Task ConfirmDialogAsync()
    {
        var dialog = Dialog;
        await _page.Locator(ConfirmButton).ClickAsync();

        // Wait for dialog to close
        await dialog.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 5000
        });
        await WaitForMenuClosedAsync();
    }

    /// <summary>
    /// The action menu that opened a dialog stays open until the dialog's result is in; wait for its
    /// popover to go so the next click lands on the page and not on the menu's overlay.
    /// </summary>
    private Task WaitForMenuClosedAsync() =>
        OpenPopover.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 5000 });

    /// <summary>
    /// Cancels the current confirmation dialog and waits for it to close.
    /// </summary>
    public async Task CancelDialogAsync()
    {
        var dialog = Dialog;
        await _page.Locator(CancelButton).ClickAsync();

        // Wait for dialog to close
        await dialog.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 5000
        });
        await WaitForMenuClosedAsync();
    }

    /// <summary>The table row for the user with <paramref name="email"/> (count 0 once the status filter hides it).</summary>
    public ILocator UserRow(string email) => UserRows.Filter(new() { HasText = email });

    /// <summary>The status chip reading <paramref name="status"/> in the user's row.</summary>
    public ILocator UserStatusChip(string email, string status) =>
        UserRow(email).Locator(".mud-chip").Filter(new() { HasText = status });

    /// <summary>
    /// The status filter: the MudSelect with Label="Status Filter", found via GetByLabel.
    /// </summary>
    public ILocator StatusFilter => _page.GetByLabel("Status Filter");

    /// <summary>The permission level chip for a user.</summary>
    public ILocator UserPermissionChip(string email) => UserRow(email).Locator($"{PermissionCell} .mud-chip");

    /// <summary>
    /// The TOTP security icon for a user. TOTP is enabled when its class contains "Success" (green).
    /// </summary>
    public ILocator UserTotpIcon(string email) => UserRow(email).Locator($"{TotpCell} .mud-icon-root");

    /// <summary>The locked indicator chip in a user's row.</summary>
    public ILocator UserLockedChip(string email) => UserRow(email).Locator(".mud-chip:has-text('Locked')");

    /// <summary>The snackbar.</summary>
    public ILocator Snackbar => _page.Locator(".mud-snackbar");

    /// <summary>The snackbar whose message contains <paramref name="text"/>.</summary>
    public ILocator SnackbarWithText(string text) => Snackbar.Filter(new() { HasText = text });

    /// <summary>
    /// Toggles <paramref name="status"/> ("Active", "Pending", "Disabled", "Deleted") in the multi-select
    /// Status Filter and closes the dropdown. The default selection is Active + Pending + Disabled.
    /// </summary>
    public async Task ToggleStatusFilterAsync(string status)
    {
        await StatusFilter.ClickAsync();
        await OpenPopover.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        await OpenPopover.Locator(".mud-list-item").Filter(new() { HasTextRegex = new Regex($@"^\s*{Regex.Escape(status)}\s*$") }).ClickAsync();

        // MudSelect handles Escape on its input; a page-level key press goes wherever focus landed.
        await StatusFilter.PressAsync("Escape");
        await OpenPopover.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 5000 });
    }

    /// <summary>
    /// The radio option labelled exactly <paramref name="level"/> ("Admin", "GlobalAdmin", "Owner") in the
    /// open Edit Permission dialog.
    /// </summary>
    public ILocator PermissionLevelOption(string level) =>
        Dialog.Locator(".mud-radio").Filter(new() { Has = _page.GetByText(level, new() { Exact = true }) });

    /// <summary>Picks <paramref name="level"/> in the open Edit Permission dialog.</summary>
    public Task SelectPermissionLevelAsync(string level) => PermissionLevelOption(level).ClickAsync();

    /// <summary>Clicks Save in the open dialog and waits for it to close.</summary>
    public async Task SaveDialogAsync()
    {
        var dialog = Dialog;
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 5000 });
        await WaitForMenuClosedAsync();
    }

    /// <summary>
    /// Gets the current URL.
    /// </summary>
    public string CurrentUrl => _page.Url;
}
