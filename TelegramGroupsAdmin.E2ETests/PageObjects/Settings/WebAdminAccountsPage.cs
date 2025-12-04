using Microsoft.Playwright;

namespace TelegramGroupsAdmin.E2ETests.PageObjects.Settings;

/// <summary>
/// Page object for the Web Admin Accounts settings section (/settings/system/accounts).
/// Provides methods to manage admin users, invites, and account settings.
/// </summary>
public class WebAdminAccountsPage
{
    private readonly IPage _page;

    // Selectors - Layout
    private const string PageTitle = ".mud-typography-h4";
    private const string LoadingIndicator = ".mud-progress-linear";
    private const string UserTable = ".mud-table";
    private const string UserTableBody = ".mud-table-body";
    private const string UserTableRow = ".mud-table-body tr";

    // Selectors - Action buttons (top)
    private const string CreateUserButton = "button:has-text('Create User')";
    private const string ManageInvitesButton = "button:has-text('Manage Invites')";

    // Selectors - Status filter (the MudSelect has Label="Status Filter")
    private const string StatusFilterContainer = ".mud-paper:has-text('Filter by Status')";
    private const string StatusFilterSelect = ".mud-paper:has-text('Filter by Status') .mud-select";

    // Selectors - Table headers
    private const string TableHeader = ".mud-table-head th";

    // Selectors - Table cells
    private const string EmailCell = "td[data-label='Email']";
    private const string PermissionCell = "td[data-label='Permission Level']";
    private const string StatusCell = "td[data-label='Status']";
    private const string TotpCell = "td[data-label='TOTP']";
    private const string ActionsCell = "td[data-label='Actions']";

    // Selectors - Action menu
    private const string ActionMenuButton = "button:has(.mud-icon-root[data-testid='MoreVertIcon'])";
    private const string ActionMenuItem = ".mud-menu-item, .mud-list-item";

    // Selectors - Dialogs
    private const string Dialog = ".mud-dialog";
    private const string DialogTitle = ".mud-dialog-title";
    private const string DialogContent = ".mud-dialog-content";
    private const string DialogActions = ".mud-dialog-actions";
    private const string ConfirmButton = ".mud-dialog button:has-text('Disable'), .mud-dialog button:has-text('Delete'), .mud-dialog button:has-text('Reset'), .mud-dialog button:has-text('Unlock'), .mud-dialog button:has-text('Restore')";
    private const string CancelButton = ".mud-dialog button:has-text('Cancel')";

    // Create Invite Dialog selectors
    private const string PermissionSelect = ".mud-dialog .mud-select";
    private const string ValidDaysInput = ".mud-dialog .mud-input input[type='number']";
    private const string CreateInviteDialogButton = ".mud-dialog button:has-text('Create Invite')";
    private const string InviteLinkText = ".mud-dialog .mud-typography:has-text('register?invite=')";
    private const string CopyLinkButton = ".mud-dialog button:has-text('Copy Link')";

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
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>
    /// Waits for the page to finish loading.
    /// </summary>
    public async Task WaitForLoadAsync(int timeoutMs = 15000)
    {
        // Wait for table to be visible
        await _page.Locator(UserTable).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = timeoutMs
        });

        // Wait for loading indicator to disappear
        var loadingIndicator = _page.Locator(LoadingIndicator);
        try
        {
            await loadingIndicator.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Hidden,
                Timeout = 5000
            });
        }
        catch (TimeoutException)
        {
            // Loading indicator may have already disappeared
        }
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
    /// Checks if the user table is visible.
    /// </summary>
    public async Task<bool> IsUserTableVisibleAsync()
    {
        return await _page.Locator(UserTable).IsVisibleAsync();
    }

    /// <summary>
    /// Gets the table headers.
    /// </summary>
    public async Task<List<string>> GetTableHeadersAsync()
    {
        var headers = new List<string>();
        var headerElements = await _page.Locator(TableHeader).AllAsync();
        foreach (var header in headerElements)
        {
            var text = await header.TextContentAsync();
            if (!string.IsNullOrWhiteSpace(text))
                headers.Add(text.Trim());
        }
        return headers;
    }

    /// <summary>
    /// Gets the count of users displayed in the table.
    /// </summary>
    public async Task<int> GetUserCountAsync()
    {
        return await _page.Locator(UserTableRow).CountAsync();
    }

    /// <summary>
    /// Gets all user emails displayed in the table.
    /// </summary>
    public async Task<List<string>> GetUserEmailsAsync()
    {
        var emails = new List<string>();
        var rows = await _page.Locator(UserTableRow).AllAsync();
        foreach (var row in rows)
        {
            var emailCell = row.Locator(EmailCell);
            var text = await emailCell.TextContentAsync();
            if (!string.IsNullOrWhiteSpace(text))
                emails.Add(text.Trim());
        }
        return emails;
    }

    /// <summary>
    /// Checks if the Create User button is visible.
    /// </summary>
    public async Task<bool> IsCreateUserButtonVisibleAsync()
    {
        return await _page.Locator(CreateUserButton).IsVisibleAsync();
    }

    /// <summary>
    /// Checks if the Manage Invites button is visible.
    /// </summary>
    public async Task<bool> IsManageInvitesButtonVisibleAsync()
    {
        return await _page.Locator(ManageInvitesButton).IsVisibleAsync();
    }

    /// <summary>
    /// Clicks the Create User button to open the invite dialog.
    /// Waits for dialog to be visible before returning.
    /// </summary>
    public async Task ClickCreateUserAsync()
    {
        await _page.Locator(CreateUserButton).ClickAsync();

        // Wait for dialog to appear using Playwright's auto-waiting
        await _page.Locator(Dialog).WaitForAsync(new LocatorWaitForOptions
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
        await _page.Locator(ManageInvitesButton).ClickAsync();

        // Wait for dialog to appear using Playwright's auto-waiting
        await _page.Locator(Dialog).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
    }

    /// <summary>
    /// Checks if a dialog is currently open.
    /// </summary>
    public async Task<bool> IsDialogOpenAsync()
    {
        return await _page.Locator(Dialog).IsVisibleAsync();
    }

    /// <summary>
    /// Gets the title of the currently open dialog.
    /// </summary>
    public async Task<string?> GetDialogTitleAsync()
    {
        return await _page.Locator(DialogTitle).TextContentAsync();
    }

    /// <summary>
    /// Gets the permission options available in the create invite dialog.
    /// Scopes the search to within the dialog to avoid matching other selects on the page.
    /// </summary>
    public async Task<List<string>> GetPermissionOptionsAsync()
    {
        // Scope to the dialog to avoid matching the status filter select outside the dialog
        var dialog = _page.Locator(Dialog);

        // Find the Permission Level select within the dialog using the label
        // MudSelect creates an input with the label, we need to click on the select container
        var selectContainer = dialog.Locator(".mud-select").First;
        await selectContainer.ClickAsync();

        // Wait for the popover to open - Playwright's auto-waiting handles this
        var popover = _page.Locator(".mud-popover-open");
        await popover.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });

        // Get all options from the dropdown popover
        var options = new List<string>();
        var optionElements = await popover.Locator(".mud-list-item").AllAsync();

        foreach (var option in optionElements)
        {
            var text = await option.TextContentAsync();
            if (!string.IsNullOrWhiteSpace(text))
                options.Add(text.Trim());
        }

        // Close dropdown by pressing escape and wait for popover to close
        await _page.Keyboard.PressAsync("Escape");
        await popover.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 2000
        });

        return options;
    }

    /// <summary>
    /// Closes the current dialog and waits for it to disappear.
    /// </summary>
    public async Task CloseDialogAsync()
    {
        var dialog = _page.Locator(Dialog);

        // Try clicking cancel, if not available press escape
        var cancelButton = _page.Locator(CancelButton);
        if (await cancelButton.IsVisibleAsync())
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
        var row = _page.Locator(UserTableRow).Filter(new() { HasText = email });
        var menuButton = row.Locator(ActionMenuButton);
        await menuButton.ClickAsync();

        // Wait for menu popover to appear
        var menuPopover = _page.Locator(".mud-popover-open");
        await menuPopover.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
    }

    /// <summary>
    /// Gets the available action menu items.
    /// </summary>
    public async Task<List<string>> GetActionMenuItemsAsync()
    {
        var items = new List<string>();
        var menuItems = await _page.Locator(".mud-popover .mud-list-item, .mud-menu .mud-menu-item").AllAsync();
        foreach (var item in menuItems)
        {
            var text = await item.TextContentAsync();
            if (!string.IsNullOrWhiteSpace(text))
                items.Add(text.Trim());
        }
        return items;
    }

    /// <summary>
    /// Clicks an action menu item by text and waits for menu to close.
    /// </summary>
    public async Task ClickActionMenuItemAsync(string itemText)
    {
        var menuPopover = _page.Locator(".mud-popover-open");
        var menuItem = _page.Locator($".mud-popover .mud-list-item:has-text('{itemText}'), .mud-menu .mud-menu-item:has-text('{itemText}')");
        await menuItem.ClickAsync();

        // Wait for menu to close
        await menuPopover.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 5000
        });
    }

    /// <summary>
    /// Confirms the current confirmation dialog and waits for it to close.
    /// </summary>
    public async Task ConfirmDialogAsync()
    {
        var dialog = _page.Locator(Dialog);
        await _page.Locator(ConfirmButton).ClickAsync();

        // Wait for dialog to close
        await dialog.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 5000
        });
    }

    /// <summary>
    /// Cancels the current confirmation dialog and waits for it to close.
    /// </summary>
    public async Task CancelDialogAsync()
    {
        var dialog = _page.Locator(Dialog);
        await _page.Locator(CancelButton).ClickAsync();

        // Wait for dialog to close
        await dialog.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 5000
        });
    }

    /// <summary>
    /// Checks if a user has a specific status chip.
    /// </summary>
    public async Task<bool> UserHasStatusAsync(string email, string status)
    {
        var row = _page.Locator(UserTableRow).Filter(new() { HasText = email });
        var statusChip = row.Locator($".mud-chip:has-text('{status}')");
        return await statusChip.IsVisibleAsync();
    }

    /// <summary>
    /// Checks if the status filter is visible.
    /// Uses GetByLabel to find the MudSelect with Label="Status Filter".
    /// </summary>
    public async Task<bool> IsStatusFilterVisibleAsync()
    {
        // The MudSelect has Label="Status Filter", so use GetByLabel which matches the label text
        var statusFilter = _page.GetByLabel("Status Filter");
        return await statusFilter.IsVisibleAsync();
    }

    /// <summary>
    /// Gets the permission level chip text for a user.
    /// </summary>
    public async Task<string?> GetUserPermissionLevelAsync(string email)
    {
        var row = _page.Locator(UserTableRow).Filter(new() { HasText = email });
        var permissionChip = row.Locator($"{PermissionCell} .mud-chip");
        return await permissionChip.TextContentAsync();
    }

    /// <summary>
    /// Checks if a user has TOTP enabled (security icon is green).
    /// </summary>
    public async Task<bool> UserHasTotpEnabledAsync(string email)
    {
        var row = _page.Locator(UserTableRow).Filter(new() { HasText = email });
        var securityIcon = row.Locator($"{TotpCell} .mud-icon-root");
        var colorClass = await securityIcon.GetAttributeAsync("class");
        return colorClass?.Contains("Success") ?? false;
    }

    /// <summary>
    /// Checks if a user row shows the locked indicator.
    /// </summary>
    public async Task<bool> UserIsLockedAsync(string email)
    {
        var row = _page.Locator(UserTableRow).Filter(new() { HasText = email });
        var lockedChip = row.Locator(".mud-chip:has-text('Locked')");
        return await lockedChip.IsVisibleAsync();
    }

    /// <summary>
    /// Waits for a snackbar message to appear.
    /// </summary>
    public async Task<string?> WaitForSnackbarAsync(int timeoutMs = 5000)
    {
        var snackbar = _page.Locator(".mud-snackbar");
        await snackbar.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = timeoutMs
        });
        return await snackbar.TextContentAsync();
    }

    /// <summary>
    /// Gets the current URL.
    /// </summary>
    public string CurrentUrl => _page.Url;
}
