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
}
