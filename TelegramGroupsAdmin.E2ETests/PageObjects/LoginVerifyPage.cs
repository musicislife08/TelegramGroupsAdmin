using Microsoft.Playwright;

namespace TelegramGroupsAdmin.E2ETests.PageObjects;

/// <summary>
/// Page object for LoginVerify.razor (static SSR page for TOTP verification).
/// This page appears after password login when 2FA is enabled.
/// </summary>
public class LoginVerifyPage
{
    private readonly IPage _page;

    // Selectors - LoginVerify.razor uses plain HTML inputs (static SSR)
    private const string CodeInput = "input#code";
    private const string SubmitButton = "button[type='submit']";
    private const string ErrorAlert = ".alert-error";
    private const string SuccessAlert = ".alert-success";
    private const string BackToLoginLink = "a[href='/login']";

    // Recovery code selectors
    private const string UseRecoveryCodeLink = "a.recovery-link:has-text('Use a recovery code instead')";
    private const string RecoveryCodeInput = "input#recoveryCode";
    private const string BackToAuthenticatorLink = "a.recovery-link:has-text('Back to authenticator code')";

    public LoginVerifyPage(IPage page)
    {
        _page = page;
    }

    /// <summary>
    /// Waits for the TOTP verification page to load.
    /// Call this after login redirects to /login/verify.
    /// </summary>
    public async Task WaitForPageAsync(int timeoutMs = 10000)
    {
        await _page.WaitForURLAsync("**/login/verify**", new PageWaitForURLOptions
        {
            Timeout = timeoutMs
        });
        await _page.WaitForSelectorAsync(CodeInput);
    }

    /// <summary>
    /// Fills in the 6-digit TOTP code.
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
    /// </summary>
    public async Task<string?> GetErrorMessageAsync(int timeoutMs = 5000)
    {
        try
        {
            await _page.WaitForSelectorAsync(ErrorAlert, new PageWaitForSelectorOptions
            {
                Timeout = timeoutMs
            });
            return await _page.TextContentAsync(ErrorAlert);
        }
        catch (TimeoutException)
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
    /// Waits for redirect away from verify page (successful verification).
    /// </summary>
    public async Task WaitForRedirectAsync(int timeoutMs = 10000)
    {
        await _page.WaitForURLAsync(url => !url.Contains("/login/verify"), new PageWaitForURLOptions
        {
            Timeout = timeoutMs
        });
    }

    /// <summary>
    /// Clicks the back to login link.
    /// </summary>
    public async Task ClickBackToLoginAsync()
    {
        await _page.ClickAsync(BackToLoginLink);
    }

    #region Recovery Code Methods

    /// <summary>
    /// Checks if the "Use a recovery code instead" link is visible.
    /// </summary>
    public async Task<bool> IsUseRecoveryCodeLinkVisibleAsync()
    {
        return await _page.Locator(UseRecoveryCodeLink).IsVisibleAsync();
    }

    /// <summary>
    /// Clicks the "Use a recovery code instead" link.
    /// </summary>
    public async Task ClickUseRecoveryCodeAsync()
    {
        await _page.ClickAsync(UseRecoveryCodeLink);
        // Small delay for navigation to complete
        await Task.Delay(500);
    }

    /// <summary>
    /// Waits for the recovery code input form to appear.
    /// </summary>
    public async Task WaitForRecoveryCodeFormAsync(int timeoutMs = 5000)
    {
        await _page.WaitForSelectorAsync(RecoveryCodeInput, new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = timeoutMs
        });
    }

    /// <summary>
    /// Checks if the recovery code input is visible.
    /// </summary>
    public async Task<bool> IsRecoveryCodeInputVisibleAsync()
    {
        return await _page.Locator(RecoveryCodeInput).IsVisibleAsync();
    }

    /// <summary>
    /// Fills in the recovery code.
    /// </summary>
    public async Task FillRecoveryCodeAsync(string recoveryCode)
    {
        await _page.FillAsync(RecoveryCodeInput, recoveryCode);
    }

    /// <summary>
    /// Performs complete recovery code verification: fill code and submit.
    /// </summary>
    public async Task VerifyWithRecoveryCodeAsync(string recoveryCode)
    {
        await FillRecoveryCodeAsync(recoveryCode);
        await SubmitAsync();
    }

    /// <summary>
    /// Clicks the "Back to authenticator code" link.
    /// </summary>
    public async Task ClickBackToAuthenticatorAsync()
    {
        await _page.ClickAsync(BackToAuthenticatorLink);
    }

    /// <summary>
    /// Checks if the "Back to authenticator code" link is visible.
    /// </summary>
    public async Task<bool> IsBackToAuthenticatorLinkVisibleAsync()
    {
        return await _page.Locator(BackToAuthenticatorLink).IsVisibleAsync();
    }

    /// <summary>
    /// Performs the complete flow: click recovery code link, wait for form, enter code, submit.
    /// </summary>
    public async Task LoginWithRecoveryCodeAsync(string recoveryCode)
    {
        await ClickUseRecoveryCodeAsync();
        await WaitForRecoveryCodeFormAsync();
        await VerifyWithRecoveryCodeAsync(recoveryCode);
    }

    #endregion
}
