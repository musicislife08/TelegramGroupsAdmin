using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

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
    private const string ErrorAlertSelector = ".alert-error";
    private const string BackToLoginLink = "a[href='/login']";

    // Recovery code selectors
    private const string UseRecoveryCodeLinkSelector = "a.recovery-link:has-text('Use a recovery code instead')";
    private const string RecoveryCodeInputSelector = "input#recoveryCode";
    private const string BackToAuthenticatorLinkSelector = "a.recovery-link:has-text('Back to authenticator code')";

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
        // WaitUntil=Commit waits only for the navigation to commit, not the full "Load"
        // event (all sub-resources). The CodeInput assertion below is the real readiness
        // signal and auto-retries; waiting for "Load" was the source of CI nav-timeout flakes.
        await _page.WaitForURLAsync("**/login/verify**", new PageWaitForURLOptions
        {
            Timeout = timeoutMs,
            WaitUntil = WaitUntilState.Commit
        });
        await Expect(_page.Locator(CodeInput)).ToBeVisibleAsync(new() { Timeout = timeoutMs });
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

    /// <summary>The error alert (e.g. invalid TOTP or recovery code).</summary>
    public ILocator ErrorAlert => _page.Locator(ErrorAlertSelector);

    /// <summary>
    /// Waits for redirect away from verify page (successful verification).
    /// </summary>
    public async Task WaitForRedirectAsync(int timeoutMs = 10000)
    {
        // Commit, not Load: the redirect is done once the new URL commits; waiting for the
        // destination's full Load event under contention is what times out.
        await _page.WaitForURLAsync(url => !url.Contains("/login/verify"), new PageWaitForURLOptions
        {
            Timeout = timeoutMs,
            WaitUntil = WaitUntilState.Commit
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

    /// <summary>The authenticator (TOTP) code input, shown on the default verification form.</summary>
    public ILocator TotpCodeInput => _page.Locator(CodeInput);

    /// <summary>The "Use a recovery code instead" link.</summary>
    public ILocator UseRecoveryCodeLink => _page.Locator(UseRecoveryCodeLinkSelector);

    /// <summary>The recovery code input, shown after choosing to use a recovery code.</summary>
    public ILocator RecoveryCodeInput => _page.Locator(RecoveryCodeInputSelector);

    /// <summary>The "Back to authenticator code" link, shown on the recovery code form.</summary>
    public ILocator BackToAuthenticatorLink => _page.Locator(BackToAuthenticatorLinkSelector);

    /// <summary>
    /// Clicks the "Use a recovery code instead" link.
    /// </summary>
    public async Task ClickUseRecoveryCodeAsync()
    {
        await _page.ClickAsync(UseRecoveryCodeLinkSelector);
        await Expect(RecoveryCodeInput).ToBeVisibleAsync();
    }

    /// <summary>
    /// Waits for the recovery code input form to appear.
    /// </summary>
    public async Task WaitForRecoveryCodeFormAsync(int timeoutMs = 5000)
    {
        await _page.WaitForSelectorAsync(RecoveryCodeInputSelector, new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = timeoutMs
        });
    }

    /// <summary>
    /// Fills in the recovery code.
    /// </summary>
    public async Task FillRecoveryCodeAsync(string recoveryCode)
    {
        await _page.FillAsync(RecoveryCodeInputSelector, recoveryCode);
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
    /// Clicks the "Back to authenticator code" link and waits for the authenticator form to render.
    /// The link is a full navigation (static SSR page), so waiting on the TOTP input keeps later
    /// assertions from running against the unloading recovery form.
    /// </summary>
    public async Task ClickBackToAuthenticatorAsync()
    {
        await _page.ClickAsync(BackToAuthenticatorLinkSelector);
        await Expect(TotpCodeInput).ToBeVisibleAsync();
    }

    /// <summary>
    /// Performs the complete flow: click recovery code link, wait for form, enter code, submit.
    /// </summary>
    public async Task LoginWithRecoveryCodeAsync(string recoveryCode)
    {
        await ClickUseRecoveryCodeAsync();
        await VerifyWithRecoveryCodeAsync(recoveryCode);
    }

    #endregion
}
