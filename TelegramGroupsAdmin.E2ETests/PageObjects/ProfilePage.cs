using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.PageObjects;

/// <summary>
/// Page object for Profile.razor (/profile - the user profile settings page).
/// Provides methods to interact with account info, password change, TOTP, and Telegram linking.
/// </summary>
public class ProfilePage
{
    private readonly IPage _page;

    // Navigation
    private const string BasePath = "/profile";

    // Page elements
    private const string PageTitle = ".mud-typography-h4";
    private const string LoadingIndicator = ".mud-progress-linear";

    // Section selectors
    private const string AccountInfoSection = ".mud-paper:has(.mud-typography-h6:has-text('Account Information'))";
    private const string ChangePasswordSection = ".mud-paper:has(.mud-typography-h6:has-text('Change Password'))";
    private const string TotpSection = ".mud-paper:has(.mud-typography-h6:has-text('Two-Factor Authentication'))";
    private const string TelegramLinkingSection = ".mud-paper:has(.mud-typography-h6:has-text('Linked Telegram Accounts'))";

    public ProfilePage(IPage page)
    {
        _page = page;
    }

    #region Navigation

    /// <summary>
    /// Navigates to the profile page.
    /// </summary>
    public async Task NavigateAsync()
    {
        await _page.GotoAsync(BasePath);
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>
    /// Waits for the page to fully load.
    /// </summary>
    public async Task WaitForLoadAsync(int timeoutMs = 15000)
    {
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    #endregion

    #region Page Title

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
    /// </summary>
    public async Task<string?> GetDisplayedEmailAsync()
    {
        var input = _page.Locator($"{AccountInfoSection} .mud-input-root:has-text('Email') input");
        return await input.InputValueAsync();
    }

    /// <summary>
    /// Gets the permission level displayed in the Account Information section.
    /// </summary>
    public async Task<string?> GetDisplayedPermissionLevelAsync()
    {
        var input = _page.Locator($"{AccountInfoSection} .mud-input-root:has-text('Permission Level') input");
        return await input.InputValueAsync();
    }

    /// <summary>
    /// Checks if all account info fields are visible.
    /// </summary>
    public async Task<bool> HasAccountInfoFieldsAsync()
    {
        var emailField = _page.Locator($"{AccountInfoSection}").GetByLabel("Email");
        var permissionField = _page.Locator($"{AccountInfoSection}").GetByLabel("Permission Level");
        var createdField = _page.Locator($"{AccountInfoSection}").GetByLabel("Account Created");
        var lastLoginField = _page.Locator($"{AccountInfoSection}").GetByLabel("Last Login");

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
    /// </summary>
    public async Task FillCurrentPasswordAsync(string password)
    {
        var input = _page.Locator($"{ChangePasswordSection} .mud-input-control:has(label:text('Current Password')) input").First;
        await input.FillAsync(password);
    }

    /// <summary>
    /// Fills the new password field.
    /// Use exact: false to handle MudBlazor's label with asterisk.
    /// </summary>
    public async Task FillNewPasswordAsync(string password)
    {
        // MudBlazor labels may have extra content - use a more specific locator
        var input = _page.Locator($"{ChangePasswordSection} .mud-input-control:has(label:text('New Password')) input").First;
        await input.FillAsync(password);
    }

    /// <summary>
    /// Fills the confirm password field.
    /// </summary>
    public async Task FillConfirmPasswordAsync(string password)
    {
        var input = _page.Locator($"{ChangePasswordSection} .mud-input-control:has(label:text('Confirm New Password')) input").First;
        await input.FillAsync(password);
    }

    /// <summary>
    /// Clicks the Change Password button.
    /// </summary>
    public async Task ClickChangePasswordButtonAsync()
    {
        var button = _page.Locator(ChangePasswordSection).GetByRole(AriaRole.Button, new() { Name = "Change Password" });
        await button.ClickAsync();
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
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
    /// </summary>
    public async Task<string> GetCurrentPasswordValueAsync()
    {
        var input = _page.Locator($"{ChangePasswordSection} .mud-input-control:has(label:text('Current Password')) input").First;
        return await input.InputValueAsync();
    }

    /// <summary>
    /// Gets the new password field value.
    /// </summary>
    public async Task<string> GetNewPasswordValueAsync()
    {
        var input = _page.Locator($"{ChangePasswordSection} .mud-input-control:has(label:text('New Password')) input").First;
        return await input.InputValueAsync();
    }

    #endregion

    #region TOTP Section

    // Recovery Codes dialog selectors
    private const string PasswordConfirmDialog = ".mud-dialog:has-text('Confirm Your Password')";
    private const string RecoveryCodesDialog = ".mud-dialog:has-text('Save Your Recovery Codes')";
    private const string RecoveryCodeItems = ".mud-dialog .mud-grid-item"; // MudItem renders as mud-grid-item

    /// <summary>
    /// Checks if the TOTP section is visible.
    /// </summary>
    public async Task<bool> IsTotpSectionVisibleAsync()
    {
        return await _page.Locator(TotpSection).IsVisibleAsync();
    }

    /// <summary>
    /// Checks if TOTP is currently enabled (shows "2FA is currently enabled" alert).
    /// MudBlazor alerts default to Variant.Text, generating .mud-alert-text-* classes.
    /// </summary>
    public async Task<bool> IsTotpEnabledAsync()
    {
        var enabledAlert = _page.Locator(TotpSection).Locator(".mud-alert:has-text('2FA is currently enabled')");
        return await enabledAlert.IsVisibleAsync();
    }

    /// <summary>
    /// Checks if TOTP is currently disabled (shows "2FA is not enabled" warning).
    /// </summary>
    public async Task<bool> IsTotpDisabledAsync()
    {
        var disabledAlert = _page.Locator(TotpSection).Locator(".mud-alert:has-text('2FA is not enabled')");
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
    /// </summary>
    public async Task<bool> IsTotpSetupDialogVisibleAsync()
    {
        var dialog = _page.Locator(".mud-dialog:has-text('Enable Two-Factor Authentication')");
        return await dialog.IsVisibleAsync();
    }

    /// <summary>
    /// Checks if the QR code is visible in the TOTP setup dialog.
    /// </summary>
    public async Task<bool> IsTotpQRCodeVisibleAsync()
    {
        var qrCode = _page.Locator(".mud-dialog img[alt='QR Code']");
        return await qrCode.IsVisibleAsync();
    }

    /// <summary>
    /// Checks if the manual entry key field is visible in the TOTP setup dialog.
    /// </summary>
    public async Task<bool> IsTotpManualKeyVisibleAsync()
    {
        var dialog = _page.Locator(".mud-dialog");
        var manualKeyText = dialog.GetByText("Or enter this code manually:");
        return await manualKeyText.IsVisibleAsync();
    }

    /// <summary>
    /// Gets the verification code input field locator.
    /// </summary>
    public ILocator GetTotpVerificationCodeInput()
    {
        return _page.Locator(".mud-dialog").GetByLabel("Verification Code");
    }

    /// <summary>
    /// Closes the TOTP setup dialog by clicking Cancel.
    /// </summary>
    public async Task CancelTotpSetupDialogAsync()
    {
        var cancelButton = _page.Locator(".mud-dialog").GetByRole(AriaRole.Button, new() { Name = "Cancel" });
        await cancelButton.ClickAsync();
    }

    #region Recovery Codes

    /// <summary>
    /// Checks if the "Regenerate Recovery Codes" button is visible.
    /// Only visible when 2FA is enabled.
    /// </summary>
    public async Task<bool> IsRegenerateRecoveryCodesButtonVisibleAsync()
    {
        var button = _page.Locator(TotpSection).GetByRole(AriaRole.Button, new() { Name = "Regenerate Recovery Codes" });
        return await button.IsVisibleAsync();
    }

    /// <summary>
    /// Clicks the "Regenerate Recovery Codes" button.
    /// </summary>
    public async Task ClickRegenerateRecoveryCodesAsync()
    {
        var button = _page.Locator(TotpSection).GetByRole(AriaRole.Button, new() { Name = "Regenerate Recovery Codes" });
        await button.ClickAsync();
    }

    /// <summary>
    /// Checks if the password confirmation dialog is visible.
    /// </summary>
    public async Task<bool> IsPasswordConfirmDialogVisibleAsync()
    {
        return await _page.Locator(PasswordConfirmDialog).IsVisibleAsync();
    }

    /// <summary>
    /// Waits for the password confirmation dialog to appear.
    /// </summary>
    public async Task WaitForPasswordConfirmDialogAsync(int timeoutMs = 5000)
    {
        await _page.Locator(PasswordConfirmDialog).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = timeoutMs
        });
    }

    /// <summary>
    /// Fills the password field in the confirmation dialog.
    /// </summary>
    public async Task FillPasswordConfirmDialogAsync(string password)
    {
        var input = _page.Locator(PasswordConfirmDialog).GetByLabel("Password");
        await input.FillAsync(password);
    }

    /// <summary>
    /// Clicks "Generate New Codes" in the password confirmation dialog.
    /// </summary>
    public async Task ClickGenerateNewCodesAsync()
    {
        var button = _page.Locator(PasswordConfirmDialog).GetByRole(AriaRole.Button, new() { Name = "Generate New Codes" });
        await button.ClickAsync();
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>
    /// Cancels the password confirmation dialog.
    /// </summary>
    public async Task CancelPasswordConfirmDialogAsync()
    {
        var button = _page.Locator(PasswordConfirmDialog).GetByRole(AriaRole.Button, new() { Name = "Cancel" });
        await button.ClickAsync();
    }

    /// <summary>
    /// Checks if the recovery codes display dialog is visible.
    /// </summary>
    public async Task<bool> IsRecoveryCodesDialogVisibleAsync()
    {
        return await _page.Locator(RecoveryCodesDialog).IsVisibleAsync();
    }

    /// <summary>
    /// Waits for the recovery codes dialog to appear.
    /// </summary>
    public async Task WaitForRecoveryCodesDialogAsync(int timeoutMs = 10000)
    {
        await _page.Locator(RecoveryCodesDialog).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = timeoutMs
        });
    }

    /// <summary>
    /// Gets all recovery codes displayed in the dialog.
    /// </summary>
    public async Task<List<string>> GetRecoveryCodesFromDialogAsync()
    {
        var codes = new List<string>();
        var codeElements = await _page.Locator(RecoveryCodeItems).AllAsync();

        foreach (var element in codeElements)
        {
            var text = await element.TextContentAsync();
            if (!string.IsNullOrWhiteSpace(text))
            {
                codes.Add(text.Trim());
            }
        }

        return codes;
    }

    /// <summary>
    /// Gets the count of recovery codes displayed in the dialog.
    /// </summary>
    public async Task<int> GetRecoveryCodesCountAsync()
    {
        return await _page.Locator(RecoveryCodeItems).CountAsync();
    }

    /// <summary>
    /// Clicks "Copy All Codes" button in the recovery codes dialog.
    /// </summary>
    public async Task ClickCopyAllCodesAsync()
    {
        var button = _page.Locator(RecoveryCodesDialog).GetByRole(AriaRole.Button, new() { Name = "Copy All Codes" });
        await button.ClickAsync();
    }

    /// <summary>
    /// Clicks "I Have Saved My Codes" to close the recovery codes dialog.
    /// </summary>
    public async Task ClickSavedCodesAsync()
    {
        var button = _page.Locator(RecoveryCodesDialog).GetByRole(AriaRole.Button, new() { Name = "I Have Saved My Codes" });
        await button.ClickAsync();
    }

    /// <summary>
    /// Performs the complete regenerate recovery codes flow:
    /// clicks button, enters password, gets codes, closes dialog.
    /// Returns the list of new recovery codes.
    /// </summary>
    public async Task<List<string>> RegenerateRecoveryCodesAsync(string password)
    {
        await ClickRegenerateRecoveryCodesAsync();
        await WaitForPasswordConfirmDialogAsync();
        await FillPasswordConfirmDialogAsync(password);
        await ClickGenerateNewCodesAsync();
        await WaitForRecoveryCodesDialogAsync();

        var codes = await GetRecoveryCodesFromDialogAsync();

        await ClickSavedCodesAsync();

        return codes;
    }

    #endregion

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
    /// </summary>
    public async Task<bool> IsNoLinkedAccountsMessageVisibleAsync()
    {
        var alert = _page.Locator(TelegramLinkingSection).Locator(".mud-alert:has-text('No Telegram accounts linked')");
        return await alert.IsVisibleAsync();
    }

    /// <summary>
    /// Checks if linked accounts table is visible (has linked accounts).
    /// </summary>
    public async Task<bool> IsLinkedAccountsTableVisibleAsync()
    {
        var table = _page.Locator(TelegramLinkingSection).Locator(".mud-table");
        return await table.IsVisibleAsync();
    }

    /// <summary>
    /// Gets the count of linked Telegram accounts.
    /// </summary>
    public async Task<int> GetLinkedAccountsCountAsync()
    {
        var rows = _page.Locator(TelegramLinkingSection).Locator(".mud-table-body tr");
        return await rows.CountAsync();
    }

    /// <summary>
    /// Gets linked account usernames from the table.
    /// </summary>
    public async Task<List<string>> GetLinkedAccountUsernamesAsync()
    {
        var usernames = new List<string>();
        var cells = await _page.Locator($"{TelegramLinkingSection} .mud-table-body td[data-label='Username']").AllAsync();

        foreach (var cell in cells)
        {
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
    /// </summary>
    public async Task<bool> HasLinkedAccountWithUsernameAsync(string username)
    {
        var cell = _page.Locator($"{TelegramLinkingSection} td[data-label='Username']:has-text('{username}')");
        return await cell.IsVisibleAsync();
    }

    /// <summary>
    /// Clicks the "Link New Telegram Account" button.
    /// </summary>
    public async Task ClickLinkNewAccountButtonAsync()
    {
        var button = _page.Locator(TelegramLinkingSection).GetByRole(AriaRole.Button, new() { Name = "Link New Telegram Account" });
        await button.ClickAsync();
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>
    /// Checks if the link token display is visible.
    /// </summary>
    public async Task<bool> IsLinkTokenVisibleAsync()
    {
        var tokenAlert = _page.Locator(TelegramLinkingSection).Locator(".mud-alert:has-text('Your Link Token')");
        return await tokenAlert.IsVisibleAsync();
    }

    /// <summary>
    /// Gets the generated link token value.
    /// </summary>
    public async Task<string?> GetLinkTokenValueAsync()
    {
        var input = _page.Locator($"{TelegramLinkingSection} .mud-alert:has-text('Your Link Token') input");
        return await input.InputValueAsync();
    }

    /// <summary>
    /// Clicks the Unlink button for a specific account row.
    /// </summary>
    public async Task ClickUnlinkButtonAsync(int rowIndex = 0)
    {
        var unlinkButton = _page.Locator($"{TelegramLinkingSection} .mud-table-body tr").Nth(rowIndex)
            .GetByRole(AriaRole.Button, new() { Name = "Unlink" });
        await unlinkButton.ClickAsync();
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    #endregion

    #region Snackbar Helpers

    /// <summary>
    /// Waits for and returns snackbar message text.
    /// </summary>
    public async Task<string?> WaitForSnackbarAsync(int timeoutMs = 5000)
    {
        var snackbar = _page.Locator(".mud-snackbar");
        await Expect(snackbar.First).ToBeVisibleAsync(new() { Timeout = timeoutMs });
        return await snackbar.First.TextContentAsync();
    }

    /// <summary>
    /// Checks if a snackbar with specific text is visible.
    /// </summary>
    public async Task<bool> HasSnackbarWithTextAsync(string text)
    {
        var snackbar = _page.Locator($".mud-snackbar:has-text('{text}')");
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
