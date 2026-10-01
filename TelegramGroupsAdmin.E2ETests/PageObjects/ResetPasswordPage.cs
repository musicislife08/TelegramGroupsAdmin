using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.PageObjects;

/// <summary>
/// Page object for ResetPassword.razor (/reset-password?token=xxx).
/// Handles the "set new password" form after clicking reset link.
/// </summary>
public class ResetPasswordPage
{
    private readonly IPage _page;

    // Selectors - MudBlazor components
    // MudAlert generates classes like .mud-alert-text-error, .mud-alert-text-success
    private const string PageTitle = ".mud-typography-h4";
    private const string ErrorAlertSelector = ".mud-alert-text-error";
    private const string SuccessAlertSelector = ".mud-alert-text-success";
    private const string SignInLink = "a[href='/login']";
    private const string RequestNewLinkButtonSelector = "a[href='/forgot-password']";

    public ResetPasswordPage(IPage page)
    {
        _page = page;
    }

    /// <summary>
    /// Navigates to the reset password page with a token.
    /// </summary>
    public async Task NavigateAsync(string token)
    {
        await _page.GotoAsync($"/reset-password?token={token}");
        await Expect(_page.Locator(PageTitle)).ToBeVisibleAsync();
    }

    /// <summary>
    /// Navigates directly from a reset link URL.
    /// </summary>
    public async Task NavigateFromLinkAsync(string resetLink)
    {
        // Extract the path from the full URL
        var uri = new Uri(resetLink);
        await _page.GotoAsync($"{uri.PathAndQuery}");
        await Expect(_page.Locator(PageTitle)).ToBeVisibleAsync();
    }

    /// <summary>
    /// Waits for the page to load.
    /// </summary>
    public async Task WaitForPageAsync(int timeoutMs = 10000)
    {
        await _page.WaitForURLAsync("**/reset-password**", new PageWaitForURLOptions
        {
            Timeout = timeoutMs
        });
    }

    /// <summary>
    /// Gets locator for the New Password input using semantic label.
    /// </summary>
    private ILocator NewPasswordInput => _page.GetByLabel("New Password");

    /// <summary>
    /// Gets locator for the Confirm Password input using semantic label.
    /// </summary>
    private ILocator ConfirmPasswordInput => _page.GetByLabel("Confirm Password");

    /// <summary>
    /// Waits for the form to be visible (token was valid).
    /// </summary>
    public async Task WaitForFormAsync(int timeoutMs = 10000)
    {
        await NewPasswordInput.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = timeoutMs
        });
    }

    /// <summary>
    /// Fills the new password field.
    /// </summary>
    public async Task FillNewPasswordAsync(string password)
    {
        await NewPasswordInput.ClickAsync();
        await NewPasswordInput.FillAsync(password);
    }

    /// <summary>
    /// Fills the confirm password field.
    /// </summary>
    public async Task FillConfirmPasswordAsync(string password)
    {
        await ConfirmPasswordInput.ClickAsync();
        await ConfirmPasswordInput.FillAsync(password);
    }

    /// <summary>
    /// Clicks the submit button.
    /// </summary>
    public async Task SubmitAsync()
    {
        await _page.GetByRole(AriaRole.Button, new() { Name = "Reset Password" }).ClickAsync();
    }

    /// <summary>
    /// Resets the password with the given credentials.
    /// </summary>
    public async Task ResetPasswordAsync(string newPassword, string confirmPassword)
    {
        await FillNewPasswordAsync(newPassword);
        await FillConfirmPasswordAsync(confirmPassword);
        await SubmitAsync();
    }

    /// <summary>
    /// Resets the password (same value for both fields).
    /// </summary>
    public async Task ResetPasswordAsync(string newPassword)
    {
        await ResetPasswordAsync(newPassword, newPassword);
    }

    /// <summary>
    /// Waits for the success message to appear.
    /// </summary>
    public async Task WaitForSuccessAsync(int timeoutMs = 10000)
    {
        await _page.WaitForSelectorAsync(SuccessAlertSelector, new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = timeoutMs
        });
    }

    /// <summary>
    /// Waits for redirect to login page (happens after successful reset).
    /// </summary>
    public async Task WaitForRedirectToLoginAsync(int timeoutMs = 15000)
    {
        await _page.WaitForURLAsync("**/login**", new PageWaitForURLOptions
        {
            Timeout = timeoutMs
        });
    }

    /// <summary>Clicks the "Sign In" link (an interactive page: the live circuit must be up first).</summary>
    public async Task ClickSignInLinkAsync()
    {
        await _page.WaitForInteractiveAsync();
        await _page.ClickAsync(SignInLink);
    }

    /// <summary>The success alert shown after the password is reset.</summary>
    public ILocator SuccessAlert => _page.Locator(SuccessAlertSelector);

    /// <summary>The error alert (e.g. mismatched/short password, invalid or missing token).</summary>
    public ILocator ErrorAlert => _page.Locator(ErrorAlertSelector);

    /// <summary>The "Request New Link" button, shown when the token is invalid or missing.</summary>
    public ILocator RequestNewLinkButton => _page.Locator(RequestNewLinkButtonSelector);
}
