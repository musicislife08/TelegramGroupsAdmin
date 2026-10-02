using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.PageObjects;

/// <summary>
/// Page object for Setup2FA.razor (static SSR page for initial TOTP setup).
/// This page appears on first login when TOTP is required but not yet configured.
/// Requires intermediate auth token from login flow.
/// </summary>
public class TotpSetupPage
{
    private readonly IPage _page;

    // Selectors - Setup2FA.razor uses plain HTML with CSS classes
    private const string PageTitleSelector = ".setup-title";
    private const string QrCodeImageSelector = ".qr-code";
    private const string ManualEntryKeySelector = ".secret-code";
    private const string CodeInput = "input#code";
    private const string SubmitButton = "button[type='submit']";
    private const string ErrorAlertSelector = ".alert-error";
    private const string SetupStepsSelector = ".setup-steps";

    // Recovery Codes selectors (shown after TOTP verification)
    private const string RecoveryCodesSectionSelector = ".recovery-codes-section";
    private const string RecoveryCodeItemSelector = ".recovery-code";
    private const string ConfirmCheckboxSelector = ".confirm-checkbox input[type='checkbox']";
    private const string CompleteSetupButton = ".recovery-codes-section button[type='submit']";
    private const string ConfirmValidationMessageSelector = ".confirm-checkbox .validation-message";

    public TotpSetupPage(IPage page)
    {
        _page = page;
    }

    /// <summary>
    /// Waits for the TOTP setup page to load completely.
    /// The page shows a loading spinner initially while generating the QR code.
    /// </summary>
    public async Task WaitForPageAsync(int timeoutMs = 10000)
    {
        // Commit, not the full Load event — the setup-steps/error wait below is the real
        // readiness signal and auto-retries; "Load" waits flake under CI contention.
        await _page.WaitForURLAsync("**/login/setup-2fa**", new PageWaitForURLOptions
        {
            Timeout = timeoutMs,
            WaitUntil = WaitUntilState.Commit
        });

        // Wait for either the setup steps to load or an error message
        await Expect(SetupSteps.Or(ErrorAlert).First).ToBeVisibleAsync(new() { Timeout = timeoutMs });
    }

    /// <summary>The page title.</summary>
    public ILocator PageTitle => _page.Locator(PageTitleSelector);

    /// <summary>The QR code image; its src is a base64 data URL.</summary>
    public ILocator QrCode => _page.Locator(QrCodeImageSelector);

    /// <summary>The manual entry key (Base32 secret) for users who can't scan the QR code.</summary>
    public ILocator ManualKey => _page.Locator(ManualEntryKeySelector);

    /// <summary>The error alert.</summary>
    public ILocator ErrorAlert => _page.Locator(ErrorAlertSelector);

    /// <summary>The setup steps (their presence indicates the page loaded successfully).</summary>
    public ILocator SetupSteps => _page.Locator(SetupStepsSelector);

    /// <summary>
    /// Reads the manual entry key text so a test can generate a TOTP code from it.
    /// Waits (auto-retrying) for the key to render before reading it.
    /// </summary>
    public async Task<string> GetManualKeyAsync()
    {
        await Expect(ManualKey).Not.ToBeEmptyAsync();
#pragma warning disable RS0030 // The secret is read to generate a TOTP code, not asserted; the Expect above synced on it
        var key = await ManualKey.TextContentAsync();
#pragma warning restore RS0030
        return key ?? string.Empty;
    }

    /// <summary>
    /// Fills in the 6-digit TOTP verification code.
    /// </summary>
    public async Task FillCodeAsync(string code)
    {
        await _page.FillAsync(CodeInput, code);
    }

    /// <summary>
    /// Clicks the verify button.
    /// </summary>
    public async Task SubmitAsync()
    {
        await _page.ClickAsync(SubmitButton);
    }

    /// <summary>
    /// Performs complete TOTP verification: fill code and submit.
    /// </summary>
    public async Task VerifyAsync(string code)
    {
        await FillCodeAsync(code);
        await SubmitAsync();
    }

    /// <summary>
    /// Waits for redirect away from setup page (successful setup).
    /// </summary>
    public async Task WaitForRedirectAsync(int timeoutMs = 10000)
    {
        await _page.WaitForURLAsync(url => !url.Contains("/login/setup-2fa"), new PageWaitForURLOptions
        {
            Timeout = timeoutMs,
            WaitUntil = WaitUntilState.Commit
        });
    }

    #region Recovery Codes

    /// <summary>
    /// The recovery codes section. This appears after successful TOTP verification.
    /// </summary>
    public ILocator RecoveryCodesSection => _page.Locator(RecoveryCodesSectionSelector);

    /// <summary>The individual recovery codes displayed on the page.</summary>
    public ILocator RecoveryCodes => _page.Locator(RecoveryCodeItemSelector);

    /// <summary>The checkbox confirming the recovery codes have been saved.</summary>
    public ILocator ConfirmCheckbox => _page.Locator(ConfirmCheckboxSelector);

    /// <summary>
    /// Waits for the recovery codes section to appear.
    /// Call this after submitting a valid TOTP code.
    /// After TOTP verification, the server redirects to ?step=recovery,
    /// so we wait for that navigation and then for the recovery section.
    /// </summary>
    public async Task WaitForRecoveryCodesAsync(int timeoutMs = 10000)
    {
        // Wait for the redirect to the recovery step
        await _page.WaitForURLAsync("**/login/setup-2fa**step=recovery**", new PageWaitForURLOptions
        {
            Timeout = timeoutMs
        });

        // Wait for the recovery section to be visible
        await RecoveryCodesSection.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = timeoutMs
        });
    }

    /// <summary>
    /// Reads all the recovery codes displayed on the page so a test can later log in with one.
    /// Waits (auto-retrying) for the codes to render before reading them.
    /// </summary>
    public async Task<List<string>> GetRecoveryCodesAsync()
    {
        await Expect(RecoveryCodes.First).Not.ToBeEmptyAsync();
#pragma warning disable RS0030 // Codes are read to be typed back in on a later login, not asserted; the Expect above synced on them (static SSR renders them all at once)
        var texts = await RecoveryCodes.AllTextContentsAsync();
#pragma warning restore RS0030
        return texts
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text.Trim())
            .ToList();
    }

    /// <summary>
    /// Checks the confirmation checkbox to confirm recovery codes are saved.
    /// </summary>
    public async Task CheckConfirmationAsync()
    {
        // CheckAsync is a no-op when the box is already checked.
        await ConfirmCheckbox.CheckAsync();
    }

    /// <summary>The "Complete Setup" submit button of the recovery codes form.</summary>
    public ILocator CompleteSetupButtonLocator => _page.Locator(CompleteSetupButton);

    /// <summary>
    /// The DataAnnotations validation message under the confirmation checkbox, rendered after the
    /// form is posted without the box checked.
    /// </summary>
    public ILocator ConfirmValidationMessage => _page.Locator(ConfirmValidationMessageSelector);

    /// <summary>
    /// Clicks the "Complete Setup" button.
    /// </summary>
    public async Task ClickCompleteSetupAsync()
    {
        await CompleteSetupButtonLocator.ClickAsync();
    }

    /// <summary>
    /// Completes the recovery codes confirmation step:
    /// checks the confirmation box and clicks complete setup.
    /// </summary>
    public async Task ConfirmRecoveryCodesAndCompleteAsync()
    {
        await CheckConfirmationAsync();
        await ClickCompleteSetupAsync();
    }

    /// <summary>
    /// Performs the complete 2FA setup flow from code entry through recovery codes confirmation.
    /// Returns the list of recovery codes for later use in tests.
    /// </summary>
    public async Task<List<string>> Complete2FASetupAsync(string totpCode)
    {
        // Enter and verify TOTP code
        await VerifyAsync(totpCode);

        // Wait for recovery codes to appear
        await WaitForRecoveryCodesAsync();

        // Get the recovery codes before confirming
        var recoveryCodes = await GetRecoveryCodesAsync();

        // Confirm and complete setup
        await ConfirmRecoveryCodesAndCompleteAsync();

        return recoveryCodes;
    }

    #endregion
}
