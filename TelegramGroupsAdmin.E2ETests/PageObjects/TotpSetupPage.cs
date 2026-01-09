using Microsoft.Playwright;

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
    private const string PageTitle = ".setup-title";
    private const string QrCodeImage = ".qr-code";
    private const string ManualEntryKey = ".secret-code";
    private const string CodeInput = "input#code";
    private const string SubmitButton = "button[type='submit']";
    private const string ErrorAlert = ".alert-error";
    private const string LoadingSpinner = ".spinner";
    private const string SetupSteps = ".setup-steps";

    // Recovery Codes selectors (shown after TOTP verification)
    private const string RecoveryCodesSection = ".recovery-codes-section";
    private const string RecoveryCodesList = ".recovery-codes";
    private const string RecoveryCodeItem = ".recovery-code";
    private const string ConfirmCheckbox = ".confirm-checkbox input[type='checkbox']";
    private const string CompleteSetupButton = ".recovery-codes-section button[type='submit']";

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
        await _page.WaitForURLAsync("**/login/setup-2fa**", new PageWaitForURLOptions
        {
            Timeout = timeoutMs
        });

        // Wait for either the setup steps to load or an error message
        var setupLocator = _page.Locator(SetupSteps);
        var errorLocator = _page.Locator(ErrorAlert);

        try
        {
            await setupLocator.Or(errorLocator).WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = timeoutMs
            });
        }
        catch (PlaywrightException)
        {
            // May timeout if redirect happens
        }
    }

    /// <summary>
    /// Checks if the QR code is visible on the page.
    /// </summary>
    public async Task<bool> IsQrCodeVisibleAsync()
    {
        return await _page.Locator(QrCodeImage).IsVisibleAsync();
    }

    /// <summary>
    /// Gets the QR code image source (data URL).
    /// </summary>
    public async Task<string?> GetQrCodeSrcAsync()
    {
        var qrCode = _page.Locator(QrCodeImage);
        if (!await qrCode.IsVisibleAsync())
            return null;

        return await qrCode.GetAttributeAsync("src");
    }

    /// <summary>
    /// Checks if the manual entry key is visible.
    /// </summary>
    public async Task<bool> IsManualKeyVisibleAsync()
    {
        return await _page.Locator(ManualEntryKey).IsVisibleAsync();
    }

    /// <summary>
    /// Gets the manual entry key text (for users who can't scan QR code).
    /// </summary>
    public async Task<string?> GetManualKeyAsync()
    {
        var keyElement = _page.Locator(ManualEntryKey);
        if (!await keyElement.IsVisibleAsync())
            return null;

        return await keyElement.TextContentAsync();
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
    /// Waits for and returns the error message text.
    /// Returns null if no error message appears within the timeout.
    /// </summary>
    public async Task<string?> GetErrorMessageAsync(int timeoutMs = 5000)
    {
        var errorLocator = _page.Locator(ErrorAlert);

        try
        {
            await errorLocator.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = timeoutMs
            });
            return await errorLocator.TextContentAsync();
        }
        catch (PlaywrightException)
        {
            return null;
        }
    }

    /// <summary>
    /// Checks if an error message is displayed.
    /// </summary>
    public async Task<bool> HasErrorMessageAsync()
    {
        return await _page.Locator(ErrorAlert).IsVisibleAsync();
    }

    /// <summary>
    /// Waits for redirect away from setup page (successful setup).
    /// </summary>
    public async Task WaitForRedirectAsync(int timeoutMs = 10000)
    {
        await _page.WaitForURLAsync(url => !url.Contains("/login/setup-2fa"), new PageWaitForURLOptions
        {
            Timeout = timeoutMs
        });
    }

    /// <summary>
    /// Checks if the page is showing the loading state.
    /// </summary>
    public async Task<bool> IsLoadingAsync()
    {
        return await _page.Locator(LoadingSpinner).IsVisibleAsync();
    }

    /// <summary>
    /// Gets the page title text.
    /// </summary>
    public async Task<string?> GetPageTitleAsync()
    {
        return await _page.Locator(PageTitle).TextContentAsync();
    }

    /// <summary>
    /// Checks if the setup steps are visible (indicates page loaded successfully).
    /// </summary>
    public async Task<bool> AreSetupStepsVisibleAsync()
    {
        return await _page.Locator(SetupSteps).IsVisibleAsync();
    }

    #region Recovery Codes

    /// <summary>
    /// Checks if the recovery codes section is visible.
    /// This appears after successful TOTP verification.
    /// </summary>
    public async Task<bool> IsRecoveryCodesSectionVisibleAsync()
    {
        return await _page.Locator(RecoveryCodesSection).IsVisibleAsync();
    }

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
        var recoverySection = _page.Locator(RecoveryCodesSection);
        await recoverySection.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = timeoutMs
        });
    }

    /// <summary>
    /// Gets all the recovery codes displayed on the page.
    /// </summary>
    public async Task<List<string>> GetRecoveryCodesAsync()
    {
        var codes = new List<string>();
        var codeElements = await _page.Locator(RecoveryCodeItem).AllAsync();

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
    /// Gets the count of recovery codes displayed.
    /// </summary>
    public async Task<int> GetRecoveryCodesCountAsync()
    {
        return await _page.Locator(RecoveryCodeItem).CountAsync();
    }

    /// <summary>
    /// Checks if the confirmation checkbox is visible.
    /// </summary>
    public async Task<bool> IsConfirmCheckboxVisibleAsync()
    {
        return await _page.Locator(ConfirmCheckbox).IsVisibleAsync();
    }

    /// <summary>
    /// Checks the confirmation checkbox to confirm recovery codes are saved.
    /// </summary>
    public async Task CheckConfirmationAsync()
    {
        var checkbox = _page.Locator(ConfirmCheckbox);
        if (!await checkbox.IsCheckedAsync())
        {
            await checkbox.CheckAsync();
        }
    }

    /// <summary>
    /// Unchecks the confirmation checkbox.
    /// </summary>
    public async Task UncheckConfirmationAsync()
    {
        var checkbox = _page.Locator(ConfirmCheckbox);
        if (await checkbox.IsCheckedAsync())
        {
            await checkbox.UncheckAsync();
        }
    }

    /// <summary>
    /// Checks if the confirmation checkbox is checked.
    /// </summary>
    public async Task<bool> IsConfirmationCheckedAsync()
    {
        return await _page.Locator(ConfirmCheckbox).IsCheckedAsync();
    }

    /// <summary>
    /// Clicks the "Complete Setup" button.
    /// </summary>
    public async Task ClickCompleteSetupAsync()
    {
        await _page.Locator(CompleteSetupButton).ClickAsync();
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
