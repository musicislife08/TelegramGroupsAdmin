using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.PageObjects;

/// <summary>
/// Page object for the WASM Profile page (/profile).
/// Standalone implementation - no inheritance from Blazor Server page objects.
/// Uses Playwright Expect assertions with auto-retry instead of NetworkIdle waits
/// (NetworkIdle never completes due to SSE connections in WASM).
/// </summary>
public class WasmProfilePage
{
    private readonly IPage _page;

    // Navigation
    private const string BasePath = "/profile";

    // Section selectors
    private const string AccountInfoSection = ".mud-paper:has(.mud-typography-h6:has-text('Account Information'))";
    private const string ChangePasswordSection = ".mud-paper:has(.mud-typography-h6:has-text('Change Password'))";
    private const string TotpSection = ".mud-paper:has(.mud-typography-h6:has-text('Two-Factor Authentication (TOTP)'))";
    private const string TelegramLinkingSection = ".mud-paper:has(.mud-typography-h6:has-text('Linked Telegram Accounts'))";

    public WasmProfilePage(IPage page)
    {
        _page = page;
    }

    #region Navigation

    /// <summary>
    /// Navigates to the profile page and waits for content to load.
    /// Uses role-based heading visibility instead of NetworkIdle.
    /// </summary>
    public async Task NavigateAsync()
    {
        await _page.GotoAsync(BasePath);
        await Expect(GetPageHeading()).ToBeVisibleAsync();
    }

    /// <summary>
    /// Waits for the page to fully load.
    /// Uses role-based heading visibility instead of NetworkIdle.
    /// </summary>
    public async Task WaitForLoadAsync(int timeoutMs = 15000)
    {
        await Expect(GetPageHeading()).ToBeVisibleAsync(new() { Timeout = timeoutMs });
    }

    /// <summary>
    /// Gets the page heading locator using semantic role-based selection.
    /// </summary>
    private ILocator GetPageHeading() =>
        _page.GetByRole(AriaRole.Heading, new() { Name = "Profile Settings", Level = 4 });

    #endregion

    #region Page Title

    /// <summary>
    /// Checks if the page title is visible.
    /// Uses role-based selection for resilience.
    /// </summary>
    public async Task<bool> IsPageTitleVisibleAsync()
    {
        return await GetPageHeading().IsVisibleAsync();
    }

    /// <summary>
    /// Gets the page title text.
    /// Uses role-based selection for resilience.
    /// </summary>
    public async Task<string?> GetPageTitleAsync()
    {
        return await GetPageHeading().TextContentAsync();
    }

    #endregion

    #region Expect-based Section Assertions

    /// <summary>
    /// Asserts that the Account Information section is visible.
    /// Uses Playwright Expect with auto-retry.
    /// </summary>
    public async Task AssertAccountInfoSectionVisibleAsync()
    {
        await Expect(_page.Locator(AccountInfoSection)).ToBeVisibleAsync();
    }

    /// <summary>
    /// Asserts that the Change Password section is visible.
    /// Uses Playwright Expect with auto-retry.
    /// </summary>
    public async Task AssertChangePasswordSectionVisibleAsync()
    {
        await Expect(_page.Locator(ChangePasswordSection)).ToBeVisibleAsync();
    }

    /// <summary>
    /// Asserts that the TOTP section is visible.
    /// Uses Playwright Expect with auto-retry.
    /// </summary>
    public async Task AssertTotpSectionVisibleAsync()
    {
        await Expect(_page.Locator(TotpSection)).ToBeVisibleAsync();
    }

    /// <summary>
    /// Asserts that the Telegram Linking section is visible.
    /// Uses Playwright Expect with auto-retry.
    /// </summary>
    public async Task AssertTelegramLinkingSectionVisibleAsync()
    {
        await Expect(_page.Locator(TelegramLinkingSection)).ToBeVisibleAsync();
    }

    #endregion

    #region Account Information Section

    /// <summary>
    /// Checks if the Account Information section is visible.
    /// </summary>
    public async Task<bool> IsAccountInfoSectionVisibleAsync()
    {
        return await _page.Locator(AccountInfoSection).IsVisibleAsync();
    }

    /// <summary>
    /// Gets the email displayed in the Account Information section.
    /// Uses GetByLabel for resilient, semantic selection.
    /// </summary>
    public async Task<string?> GetDisplayedEmailAsync()
    {
        var input = _page.Locator(AccountInfoSection).GetByLabel("Email");
        return await input.InputValueAsync();
    }

    /// <summary>
    /// Gets the permission level displayed in the Account Information section.
    /// Uses GetByLabel for resilient, semantic selection.
    /// </summary>
    public async Task<string?> GetDisplayedPermissionLevelAsync()
    {
        var input = _page.Locator(AccountInfoSection).GetByLabel("Permission Level");
        return await input.InputValueAsync();
    }

    /// <summary>
    /// Checks if all account info fields are visible.
    /// </summary>
    public async Task<bool> HasAccountInfoFieldsAsync()
    {
        var emailField = _page.Locator(AccountInfoSection).GetByLabel("Email");
        var permissionField = _page.Locator(AccountInfoSection).GetByLabel("Permission Level");
        var createdField = _page.Locator(AccountInfoSection).GetByLabel("Account Created");
        var lastLoginField = _page.Locator(AccountInfoSection).GetByLabel("Last Login");

        return await emailField.IsVisibleAsync() &&
               await permissionField.IsVisibleAsync() &&
               await createdField.IsVisibleAsync() &&
               await lastLoginField.IsVisibleAsync();
    }

    #endregion

    #region Change Password Section

    /// <summary>
    /// Checks if the Change Password section is visible.
    /// </summary>
    public async Task<bool> IsChangePasswordSectionVisibleAsync()
    {
        return await _page.Locator(ChangePasswordSection).IsVisibleAsync();
    }

    /// <summary>
    /// Fills the current password field.
    /// Uses GetByLabel for resilient, semantic selection.
    /// </summary>
    public async Task FillCurrentPasswordAsync(string password)
    {
        var input = _page.Locator(ChangePasswordSection).GetByLabel("Current Password");
        await input.FillAsync(password);
    }

    /// <summary>
    /// Fills the new password field.
    /// Uses GetByLabel with Exact=true to avoid matching "Confirm New Password".
    /// </summary>
    public async Task FillNewPasswordAsync(string password)
    {
        var input = _page.Locator(ChangePasswordSection).GetByLabel("New Password", new() { Exact = true });
        await input.FillAsync(password);
    }

    /// <summary>
    /// Fills the confirm password field.
    /// Uses GetByLabel for resilient, semantic selection.
    /// </summary>
    public async Task FillConfirmPasswordAsync(string password)
    {
        var input = _page.Locator(ChangePasswordSection).GetByLabel("Confirm New Password");
        await input.FillAsync(password);
    }

    /// <summary>
    /// Clicks the Change Password button.
    /// Does not wait for NetworkIdle - caller should wait for snackbar.
    /// </summary>
    public async Task ClickChangePasswordButtonAsync()
    {
        var button = _page.Locator(ChangePasswordSection).GetByRole(AriaRole.Button, new() { Name = "Change Password" });
        await button.ClickAsync();
    }

    /// <summary>
    /// Changes the password by filling all fields and clicking the button.
    /// </summary>
    public async Task ChangePasswordAsync(string currentPassword, string newPassword, string confirmPassword)
    {
        await FillCurrentPasswordAsync(currentPassword);
        await FillNewPasswordAsync(newPassword);
        await FillConfirmPasswordAsync(confirmPassword);
        await ClickChangePasswordButtonAsync();
    }

    /// <summary>
    /// Gets the current password field value (for testing empty state).
    /// Uses GetByLabel for resilient, semantic selection.
    /// </summary>
    public async Task<string> GetCurrentPasswordValueAsync()
    {
        var input = _page.Locator(ChangePasswordSection).GetByLabel("Current Password");
        return await input.InputValueAsync();
    }

    /// <summary>
    /// Gets the new password field value.
    /// Uses GetByLabel with Exact=true to avoid matching "Confirm New Password".
    /// </summary>
    public async Task<string> GetNewPasswordValueAsync()
    {
        var input = _page.Locator(ChangePasswordSection).GetByLabel("New Password", new() { Exact = true });
        return await input.InputValueAsync();
    }

    #endregion

    #region TOTP Section

    /// <summary>
    /// Checks if the TOTP section is visible.
    /// </summary>
    public async Task<bool> IsTotpSectionVisibleAsync()
    {
        return await _page.Locator(TotpSection).IsVisibleAsync();
    }

    /// <summary>
    /// Checks if TOTP is currently enabled (shows "2FA is currently enabled" alert).
    /// Uses CSS class selector - MudAlert renders with .mud-alert class.
    /// Note: MudSnackbar uses role="alert", but embedded MudAlert components
    /// within page sections don't consistently use ARIA roles.
    /// </summary>
    public async Task<bool> IsTotpEnabledAsync()
    {
        var enabledAlert = _page.Locator(TotpSection)
            .Locator(".mud-alert")
            .Filter(new() { HasText = "2FA is currently enabled" });
        return await enabledAlert.IsVisibleAsync();
    }

    /// <summary>
    /// Checks if TOTP is currently disabled (shows "2FA is not enabled" warning).
    /// Uses CSS class selector - MudAlert renders with .mud-alert class.
    /// </summary>
    public async Task<bool> IsTotpDisabledAsync()
    {
        var disabledAlert = _page.Locator(TotpSection)
            .Locator(".mud-alert")
            .Filter(new() { HasText = "2FA is not enabled" });
        return await disabledAlert.IsVisibleAsync();
    }

    /// <summary>
    /// Checks if the Enable 2FA button is visible (TOTP disabled state).
    /// </summary>
    public async Task<bool> IsEnable2FAButtonVisibleAsync()
    {
        var button = _page.Locator(TotpSection).GetByRole(AriaRole.Button, new() { Name = "Enable 2FA" });
        return await button.IsVisibleAsync();
    }

    /// <summary>
    /// Checks if the Reset 2FA button is visible (TOTP enabled state).
    /// </summary>
    public async Task<bool> IsReset2FAButtonVisibleAsync()
    {
        var button = _page.Locator(TotpSection).GetByRole(AriaRole.Button, new() { Name = "Reset 2FA" });
        return await button.IsVisibleAsync();
    }

    /// <summary>
    /// Clicks the Enable 2FA button to open the setup dialog.
    /// </summary>
    public async Task ClickEnable2FAButtonAsync()
    {
        var button = _page.Locator(TotpSection).GetByRole(AriaRole.Button, new() { Name = "Enable 2FA" });
        await button.ClickAsync();
    }

    /// <summary>
    /// Checks if the TOTP setup dialog is visible.
    /// Uses GetByRole for resilient, semantic selection.
    /// </summary>
    public async Task<bool> IsTotpSetupDialogVisibleAsync()
    {
        var dialog = _page.GetByRole(AriaRole.Dialog)
            .Filter(new() { HasText = "Enable Two-Factor Authentication" });
        return await dialog.IsVisibleAsync();
    }

    /// <summary>
    /// Checks if the QR code is visible in the TOTP setup dialog.
    /// Uses GetByRole for resilient, semantic selection.
    /// </summary>
    public async Task<bool> IsTotpQRCodeVisibleAsync()
    {
        var dialog = _page.GetByRole(AriaRole.Dialog);
        var qrCode = dialog.GetByRole(AriaRole.Img, new() { Name = "QR Code" });
        return await qrCode.IsVisibleAsync();
    }

    /// <summary>
    /// Checks if the manual entry key field is visible in the TOTP setup dialog.
    /// Uses GetByRole for resilient, semantic selection.
    /// </summary>
    public async Task<bool> IsTotpManualKeyVisibleAsync()
    {
        var dialog = _page.GetByRole(AriaRole.Dialog);
        var manualKeyText = dialog.GetByText("Or enter this code manually:");
        return await manualKeyText.IsVisibleAsync();
    }

    /// <summary>
    /// Gets the verification code input field locator.
    /// Uses GetByRole and GetByLabel for resilient selection.
    /// </summary>
    public ILocator GetTotpVerificationCodeInput()
    {
        return _page.GetByRole(AriaRole.Dialog).GetByLabel("Verification Code");
    }

    /// <summary>
    /// Closes the TOTP setup dialog by clicking Cancel.
    /// Uses GetByRole for resilient, semantic selection.
    /// </summary>
    public async Task CancelTotpSetupDialogAsync()
    {
        var cancelButton = _page.GetByRole(AriaRole.Dialog)
            .GetByRole(AriaRole.Button, new() { Name = "Cancel" });
        await cancelButton.ClickAsync();
    }

    #endregion

    #region Telegram Linking Section

    /// <summary>
    /// Checks if the Telegram Linking section is visible.
    /// </summary>
    public async Task<bool> IsTelegramLinkingSectionVisibleAsync()
    {
        return await _page.Locator(TelegramLinkingSection).IsVisibleAsync();
    }

    /// <summary>
    /// Checks if the "No Telegram accounts linked" message is visible.
    /// Uses CSS class selector - MudAlert renders with .mud-alert class.
    /// </summary>
    public async Task<bool> IsNoLinkedAccountsMessageVisibleAsync()
    {
        var alert = _page.Locator(TelegramLinkingSection)
            .Locator(".mud-alert")
            .Filter(new() { HasText = "No Telegram accounts linked" });
        return await alert.IsVisibleAsync();
    }

    /// <summary>
    /// Asserts that the "No Telegram accounts linked" message is visible with auto-retry.
    /// Uses CSS class selector - MudAlert renders with .mud-alert class.
    /// </summary>
    public async Task AssertNoLinkedAccountsMessageVisibleAsync(int timeoutMs = 5000)
    {
        var alert = _page.Locator(TelegramLinkingSection)
            .Locator(".mud-alert")
            .Filter(new() { HasText = "No Telegram accounts linked" });
        await Expect(alert).ToBeVisibleAsync(new() { Timeout = timeoutMs });
    }

    /// <summary>
    /// Checks if linked accounts table is visible (has linked accounts).
    /// Uses GetByRole for resilient, semantic selection.
    /// </summary>
    public async Task<bool> IsLinkedAccountsTableVisibleAsync()
    {
        var table = _page.Locator(TelegramLinkingSection).GetByRole(AriaRole.Table);
        return await table.IsVisibleAsync();
    }

    /// <summary>
    /// Gets the count of linked Telegram accounts.
    /// Uses GetByRole for resilient, semantic selection.
    /// </summary>
    public async Task<int> GetLinkedAccountsCountAsync()
    {
        var table = _page.Locator(TelegramLinkingSection).GetByRole(AriaRole.Table);
        // Get rows from tbody only (exclude header row)
        var rows = table.GetByRole(AriaRole.Row).Filter(new() { Has = _page.GetByRole(AriaRole.Cell) });
        return await rows.CountAsync();
    }

    /// <summary>
    /// Gets linked account usernames from the table.
    /// Uses GetByRole for resilient, semantic selection.
    /// </summary>
    public async Task<List<string>> GetLinkedAccountUsernamesAsync()
    {
        var usernames = new List<string>();
        var table = _page.Locator(TelegramLinkingSection).GetByRole(AriaRole.Table);
        var rows = table.GetByRole(AriaRole.Row).Filter(new() { Has = _page.GetByRole(AriaRole.Cell) });
        var rowCount = await rows.CountAsync();

        for (var i = 0; i < rowCount; i++)
        {
            // Get the first cell (Username column) from each data row
            var cell = rows.Nth(i).GetByRole(AriaRole.Cell).First;
            var text = await cell.TextContentAsync();
            if (!string.IsNullOrEmpty(text))
            {
                usernames.Add(text.Trim());
            }
        }

        return usernames;
    }

    /// <summary>
    /// Checks if a linked account with the given username is visible.
    /// Uses GetByRole and GetByText for resilient selection.
    /// </summary>
    public async Task<bool> HasLinkedAccountWithUsernameAsync(string username)
    {
        var table = _page.Locator(TelegramLinkingSection).GetByRole(AriaRole.Table);
        var cell = table.GetByRole(AriaRole.Cell).GetByText(username);
        return await cell.IsVisibleAsync();
    }

    /// <summary>
    /// Clicks the "Link New Telegram Account" button.
    /// Does not wait for NetworkIdle - caller should wait for token alert.
    /// </summary>
    public async Task ClickLinkNewAccountButtonAsync()
    {
        var button = _page.Locator(TelegramLinkingSection).GetByRole(AriaRole.Button, new() { Name = "Link New Telegram Account" });
        await button.ClickAsync();
    }

    /// <summary>
    /// Checks if the link token display is visible.
    /// Uses CSS class selector - MudAlert renders with .mud-alert class.
    /// </summary>
    public async Task<bool> IsLinkTokenVisibleAsync()
    {
        var tokenAlert = _page.Locator(TelegramLinkingSection)
            .Locator(".mud-alert")
            .Filter(new() { HasText = "Your Link Token" });
        return await tokenAlert.IsVisibleAsync();
    }

    /// <summary>
    /// Asserts that the link token is visible with auto-retry.
    /// Uses CSS class selector - MudAlert renders with .mud-alert class.
    /// </summary>
    public async Task AssertLinkTokenVisibleAsync(int timeoutMs = 5000)
    {
        var tokenAlert = _page.Locator(TelegramLinkingSection)
            .Locator(".mud-alert")
            .Filter(new() { HasText = "Your Link Token" });
        await Expect(tokenAlert).ToBeVisibleAsync(new() { Timeout = timeoutMs });
    }

    /// <summary>
    /// Gets the generated link token value.
    /// Uses CSS class selector - MudAlert renders with .mud-alert class.
    /// </summary>
    public async Task<string?> GetLinkTokenValueAsync()
    {
        var tokenAlert = _page.Locator(TelegramLinkingSection)
            .Locator(".mud-alert")
            .Filter(new() { HasText = "Your Link Token" });
        var input = tokenAlert.GetByRole(AriaRole.Textbox);
        return await input.InputValueAsync();
    }

    /// <summary>
    /// Clicks the Unlink button for a specific account row.
    /// Does not wait for NetworkIdle - caller should wait for snackbar.
    /// Uses GetByRole for resilient, semantic selection.
    /// </summary>
    public async Task ClickUnlinkButtonAsync(int rowIndex = 0)
    {
        var table = _page.Locator(TelegramLinkingSection).GetByRole(AriaRole.Table);
        // Get data rows only (rows containing cells, not header row)
        var dataRows = table.GetByRole(AriaRole.Row).Filter(new() { Has = _page.GetByRole(AriaRole.Cell) });
        var unlinkButton = dataRows.Nth(rowIndex).GetByRole(AriaRole.Button, new() { Name = "Unlink" });
        await unlinkButton.ClickAsync();
    }

    #endregion

    #region Snackbar Helpers

    // NOTE: Snackbar selectors use CSS classes intentionally.
    // MudBlazor snackbars are rendered in a global container outside page content,
    // and CSS class selectors (.mud-snackbar) are the most reliable cross-version
    // identifier for these transient notifications. ARIA roles (status/alert) vary
    // by snackbar severity and could match other elements.

    /// <summary>
    /// Waits for and returns snackbar message text.
    /// Uses CSS class selector for reliable snackbar identification.
    /// </summary>
    public async Task<string?> WaitForSnackbarAsync(int timeoutMs = 5000)
    {
        var snackbar = _page.Locator(".mud-snackbar");
        await Expect(snackbar.First).ToBeVisibleAsync(new() { Timeout = timeoutMs });
        return await snackbar.First.TextContentAsync();
    }

    /// <summary>
    /// Checks if a snackbar with specific text is visible.
    /// Uses CSS class selector for reliable snackbar identification.
    /// </summary>
    public async Task<bool> HasSnackbarWithTextAsync(string text)
    {
        var snackbar = _page.Locator(".mud-snackbar").Filter(new() { HasText = text });
        return await snackbar.IsVisibleAsync();
    }

    #endregion

    #region Helper Properties

    /// <summary>
    /// Gets the current URL.
    /// </summary>
    public string CurrentUrl => _page.Url;

    /// <summary>
    /// Checks if we're on the profile page.
    /// </summary>
    public bool IsOnProfilePage => _page.Url.Contains("/profile");

    #endregion
}
