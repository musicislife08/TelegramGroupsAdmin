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
    private const string PageTitleSelector = ".mud-typography-h4";

    // Section selectors
    private const string AccountInfoSectionSelector = ".mud-paper:has(.mud-typography-h6:has-text('Account Information'))";
    private const string ChangePasswordSectionSelector = ".mud-paper:has(.mud-typography-h6:has-text('Change Password'))";
    private const string TotpSectionSelector = ".mud-paper:has(.mud-typography-h6:has-text('Two-Factor Authentication'))";
    private const string TotpEnabledAlertSelector = ".mud-alert:has-text('2FA is currently enabled')";
    private const string TotpDisabledAlertSelector = ".mud-alert:has-text('2FA is not enabled')";
    private const string TelegramLinkingSectionSelector = ".mud-paper:has(.mud-typography-h6:has-text('Linked Telegram Accounts'))";

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
        await Expect(PageTitle).ToBeVisibleAsync();
        // The page is prerendered with its sections loaded, then the interactive circuit re-runs
        // OnInitializedAsync and re-renders them: wait for the interactive render so the load checks
        // below never pass against prerendered HTML that is about to be replaced.
        await _page.WaitForInteractiveAsync();
        await WaitForLoadAsync();
    }

    /// <summary>
    /// Waits for the page to fully load, including all content sections past the render gate.
    /// </summary>
    public async Task WaitForLoadAsync(int timeoutMs = 15000)
    {
        await Expect(AccountInfoSection).ToBeVisibleAsync(new() { Timeout = timeoutMs });
        await Expect(ChangePasswordSection).ToBeVisibleAsync(new() { Timeout = timeoutMs });
        await Expect(TotpSection).ToBeVisibleAsync(new() { Timeout = timeoutMs });
        await Expect(TelegramLinkingSection).ToBeVisibleAsync(new() { Timeout = timeoutMs });
        // Wait for the Telegram section's content (table or empty state) to have rendered, not just the paper frame.
        var telegramNoAccounts = TelegramLinkingSection.Locator(".mud-alert");
        await LinkedAccountsTable.Or(telegramNoAccounts).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = timeoutMs
        });
    }

    #endregion

    #region Page Title

    /// <summary>The page title ("Profile Settings").</summary>
    public ILocator PageTitle => _page.Locator(PageTitleSelector);

    #endregion

    #region Account Information Section

    /// <summary>The Account Information section.</summary>
    public ILocator AccountInfoSection => _page.Locator(AccountInfoSectionSelector);

    /// <summary>
    /// Asserts (auto-retrying) that every account info field (Email, Permission Level,
    /// Account Created, Last Login) is visible in the Account Information section.
    /// </summary>
    public async Task AssertAccountInfoFieldsVisibleAsync()
    {
        await Expect(AccountInfoSection.GetByLabel("Email")).ToBeVisibleAsync();
        await Expect(AccountInfoSection.GetByLabel("Permission Level")).ToBeVisibleAsync();
        await Expect(AccountInfoSection.GetByLabel("Account Created")).ToBeVisibleAsync();
        await Expect(AccountInfoSection.GetByLabel("Last Login")).ToBeVisibleAsync();
    }

    #endregion

    #region Change Password Section

    /// <summary>The Change Password section.</summary>
    public ILocator ChangePasswordSection => _page.Locator(ChangePasswordSectionSelector);

    /// <summary>The Current Password input in the Change Password section.</summary>
    public ILocator CurrentPasswordInput =>
        _page.Locator($"{ChangePasswordSectionSelector} .mud-input-control:has(label:text('Current Password')) input").First;

    /// <summary>
    /// Fills the current password field.
    /// </summary>
    public async Task FillCurrentPasswordAsync(string password)
    {
        await CurrentPasswordInput.FillAsync(password);
    }

    /// <summary>
    /// Fills the new password field.
    /// Use exact: false to handle MudBlazor's label with asterisk.
    /// </summary>
    public async Task FillNewPasswordAsync(string password)
    {
        // MudBlazor labels may have extra content - use a more specific locator
        var input = _page.Locator($"{ChangePasswordSectionSelector} .mud-input-control:has(label:text('New Password')) input").First;
        await input.FillAsync(password);
    }

    /// <summary>
    /// Fills the confirm password field.
    /// </summary>
    public async Task FillConfirmPasswordAsync(string password)
    {
        var input = _page.Locator($"{ChangePasswordSectionSelector} .mud-input-control:has(label:text('Confirm New Password')) input").First;
        await input.FillAsync(password);
    }

    /// <summary>
    /// Clicks the Change Password button.
    /// </summary>
    public async Task ClickChangePasswordButtonAsync()
    {
        var button = ChangePasswordSection.GetByRole(AriaRole.Button, new() { Name = "Change Password" });
        await button.ClickAsync();
        // Wait for snackbar to appear (indicates operation completed)
        await Expect(Snackbar).ToBeVisibleAsync();
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

    #endregion

    #region TOTP Section

    // Recovery Codes dialog selectors
    private const string PasswordConfirmDialogSelector = ".mud-dialog:has-text('Confirm Your Password')";
    private const string RecoveryCodesDialogSelector = ".mud-dialog:has-text('Save Your Recovery Codes')";
    private const string RecoveryCodeItemsSelector = ".mud-dialog .mud-grid-item"; // MudItem renders as mud-grid-item

    /// <summary>The Two-Factor Authentication section.</summary>
    public ILocator TotpSection => _page.Locator(TotpSectionSelector);

    /// <summary>
    /// The "2FA is currently enabled" alert in the TOTP section.
    /// MudBlazor alerts default to Variant.Text, generating .mud-alert-text-* classes.
    /// </summary>
    public ILocator TotpEnabledAlert => TotpSection.Locator(TotpEnabledAlertSelector);

    /// <summary>The "2FA is not enabled" warning in the TOTP section.</summary>
    public ILocator TotpDisabledAlert => TotpSection.Locator(TotpDisabledAlertSelector);

    /// <summary>
    /// Asserts (auto-retrying) that the TOTP section shows the "enabled" alert.
    /// The alert renders asynchronously (Blazor), so a retrying assertion is required.
    /// </summary>
    public async Task AssertTotpEnabledAsync(int? timeoutMs = null)
    {
        await Expect(TotpEnabledAlert).ToBeVisibleAsync(timeoutMs is { } t ? new() { Timeout = t } : null);
    }

    /// <summary>
    /// Asserts (auto-retrying) that the TOTP section shows the "not enabled" warning.
    /// </summary>
    public async Task AssertTotpDisabledAsync(int? timeoutMs = null)
    {
        await Expect(TotpDisabledAlert).ToBeVisibleAsync(timeoutMs is { } t ? new() { Timeout = t } : null);
    }

    /// <summary>The Enable 2FA button (TOTP disabled state).</summary>
    public ILocator Enable2FAButton => TotpSection.GetByRole(AriaRole.Button, new() { Name = "Enable 2FA" });

    /// <summary>The Reset 2FA button (TOTP enabled state).</summary>
    public ILocator Reset2FAButton => TotpSection.GetByRole(AriaRole.Button, new() { Name = "Reset 2FA" });

    /// <summary>
    /// Clicks the Enable 2FA button to open the setup dialog.
    /// </summary>
    public async Task ClickEnable2FAButtonAsync()
    {
        await Enable2FAButton.ClickAsync();
    }

    /// <summary>The TOTP setup dialog.</summary>
    public ILocator TotpSetupDialog => _page.Locator(".mud-dialog:has-text('Enable Two-Factor Authentication')");

    /// <summary>The QR code in the TOTP setup dialog.</summary>
    public ILocator TotpQrCode => _page.Locator(".mud-dialog img[alt='QR Code']");

    /// <summary>The manual entry key prompt in the TOTP setup dialog.</summary>
    public ILocator TotpManualKeyText => _page.Locator(".mud-dialog").GetByText("Or enter this code manually:");

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
    /// The "Regenerate Recovery Codes" button. Only visible when 2FA is enabled.
    /// </summary>
    public ILocator RegenerateRecoveryCodesButton =>
        TotpSection.GetByRole(AriaRole.Button, new() { Name = "Regenerate Recovery Codes" });

    /// <summary>
    /// Clicks the "Regenerate Recovery Codes" button.
    /// </summary>
    public async Task ClickRegenerateRecoveryCodesAsync()
    {
        await RegenerateRecoveryCodesButton.ClickAsync();
    }

    /// <summary>The password confirmation dialog shown before regenerating recovery codes.</summary>
    public ILocator PasswordConfirmDialog => _page.Locator(PasswordConfirmDialogSelector);

    /// <summary>
    /// Waits for the password confirmation dialog to appear.
    /// </summary>
    public async Task WaitForPasswordConfirmDialogAsync(int timeoutMs = 5000)
    {
        await PasswordConfirmDialog.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = timeoutMs
        });
    }

    /// <summary>
    /// Waits for the password confirmation dialog to close.
    /// Uses Playwright's auto-waiting instead of explicit delays.
    /// </summary>
    public async Task WaitForPasswordConfirmDialogClosedAsync(int timeoutMs = 5000)
    {
        await Expect(PasswordConfirmDialog).Not.ToBeVisibleAsync(
            new() { Timeout = timeoutMs });
    }

    /// <summary>
    /// Fills the password field in the confirmation dialog.
    /// </summary>
    public async Task FillPasswordConfirmDialogAsync(string password)
    {
        var input = PasswordConfirmDialog.GetByLabel("Password");
        await input.FillAsync(password);
    }

    /// <summary>
    /// Clicks "Generate New Codes" in the password confirmation dialog.
    /// </summary>
    /// <param name="expectSuccess">If true, waits for recovery codes dialog. If false, caller handles the expected outcome.</param>
    public async Task ClickGenerateNewCodesAsync(bool expectSuccess = true)
    {
        var button = PasswordConfirmDialog.GetByRole(AriaRole.Button, new() { Name = "Generate New Codes" });
        await button.ClickAsync();

        if (expectSuccess)
        {
            // Wait for the recovery codes dialog to appear
            await Expect(RecoveryCodesDialog).ToBeVisibleAsync();
        }
    }

    /// <summary>
    /// Cancels the password confirmation dialog.
    /// </summary>
    public async Task CancelPasswordConfirmDialogAsync()
    {
        var button = PasswordConfirmDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" });
        await button.ClickAsync();
    }

    /// <summary>The recovery codes display dialog.</summary>
    public ILocator RecoveryCodesDialog => _page.Locator(RecoveryCodesDialogSelector);

    /// <summary>The individual recovery codes displayed in the dialog.</summary>
    public ILocator RecoveryCodeItems => _page.Locator(RecoveryCodeItemsSelector);

    /// <summary>
    /// Waits for the recovery codes dialog to appear.
    /// </summary>
    public async Task WaitForRecoveryCodesDialogAsync(int timeoutMs = 10000)
    {
        await RecoveryCodesDialog.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = timeoutMs
        });
    }

    /// <summary>
    /// Clicks "Copy All Codes" button in the recovery codes dialog.
    /// </summary>
    public async Task ClickCopyAllCodesAsync()
    {
        var button = RecoveryCodesDialog.GetByRole(AriaRole.Button, new() { Name = "Copy All Codes" });
        await button.ClickAsync();
    }

    /// <summary>
    /// Clicks "I Have Saved My Codes" to close the recovery codes dialog.
    /// </summary>
    public async Task ClickSavedCodesAsync()
    {
        var button = RecoveryCodesDialog.GetByRole(AriaRole.Button, new() { Name = "I Have Saved My Codes" });
        await button.ClickAsync();
    }

    #endregion

    #endregion

    #region Telegram Linking Section

    /// <summary>The Linked Telegram Accounts section.</summary>
    public ILocator TelegramLinkingSection => _page.Locator(TelegramLinkingSectionSelector);

    /// <summary>The "No Telegram accounts linked" message.</summary>
    public ILocator NoLinkedAccountsMessage =>
        TelegramLinkingSection.Locator(".mud-alert:has-text('No Telegram accounts linked')");

    /// <summary>The linked accounts table (rendered only when accounts are linked).</summary>
    public ILocator LinkedAccountsTable => TelegramLinkingSection.Locator(".mud-table");

    /// <summary>The rows of the linked accounts table, one per linked Telegram account.</summary>
    public ILocator LinkedAccountRows => TelegramLinkingSection.Locator(".mud-table-body tr");

    /// <summary>The Username cell of the linked account whose username contains <paramref name="username"/>.</summary>
    public ILocator LinkedAccountUsernameCell(string username) =>
        _page.Locator($"{TelegramLinkingSectionSelector} td[data-label='Username']").Filter(new() { HasText = username });

    /// <summary>The "Your Link Token" alert shown after generating a link token.</summary>
    public ILocator LinkTokenAlert => TelegramLinkingSection.Locator(".mud-alert:has-text('Your Link Token')");

    /// <summary>The input holding the generated link token value.</summary>
    public ILocator LinkTokenInput => _page.Locator($"{TelegramLinkingSectionSelector} .mud-alert:has-text('Your Link Token') input");

    /// <summary>
    /// Clicks the "Link New Telegram Account" button.
    /// </summary>
    public async Task ClickLinkNewAccountButtonAsync()
    {
        var button = TelegramLinkingSection.GetByRole(AriaRole.Button, new() { Name = "Link New Telegram Account" });
        await button.ClickAsync();
        // Wait for the link token alert to appear
        await Expect(LinkTokenAlert).ToBeVisibleAsync();
    }

    /// <summary>
    /// Clicks the Unlink button for a specific account row.
    /// </summary>
    public async Task ClickUnlinkButtonAsync(int rowIndex = 0)
    {
        var unlinkButton = LinkedAccountRows.Nth(rowIndex)
            .GetByRole(AriaRole.Button, new() { Name = "Unlink" });
        // Confirm the button is enabled before clicking — ensures the Blazor circuit is live and
        // the row is fully interactive, not just painted.
        await Expect(unlinkButton).ToBeEnabledAsync(new() { Timeout = 10000 });
        await unlinkButton.ClickAsync();
        // Wait for snackbar to appear (indicates operation completed).
        // If the circuit briefly dropped and swallowed the click, the button remains and we retry once.
        try
        {
            await Expect(Snackbar).ToBeVisibleAsync(new() { Timeout = 10000 });
        }
        catch (PlaywrightException)
        {
            // Circuit may have reconnected without processing the click — retry if the row is still there.
#pragma warning disable RS0030 // Optional recovery: only re-click if the swallowed click left the row in place; the snackbar Expect above already timed out
            var rowStillPresent = await unlinkButton.IsVisibleAsync();
#pragma warning restore RS0030
            if (rowStillPresent)
            {
                await Expect(unlinkButton).ToBeEnabledAsync(new() { Timeout = 10000 });
                await unlinkButton.ClickAsync();
                await Expect(Snackbar).ToBeVisibleAsync(new() { Timeout = 15000 });
            }
            else
            {
                // Row is gone — the unlink succeeded silently; re-throw to surface any other issue.
                throw;
            }
        }
    }

    #endregion

    #region Snackbar Helpers

    /// <summary>
    /// The first (oldest) snackbar. Assert on it with Expect, e.g. ToContainTextAsync.
    /// </summary>
    public ILocator Snackbar => _page.Locator(".mud-snackbar").First;

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
