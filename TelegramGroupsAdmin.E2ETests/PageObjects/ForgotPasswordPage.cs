using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.PageObjects;

/// <summary>
/// Page object for ForgotPassword.razor (/forgot-password).
/// Handles the "request password reset" form.
/// </summary>
public class ForgotPasswordPage
{
    private readonly IPage _page;

    // Selectors - MudBlazor components
    // MudAlert generates classes like .mud-alert-text-error, .mud-alert-text-success
    private const string PageTitleSelector = ".mud-typography-h4";
    private const string ErrorAlertSelector = ".mud-alert-text-error";
    private const string SuccessAlertSelector = ".mud-alert-text-success";
    private const string SignInLink = "a[href='/login']";

    public ForgotPasswordPage(IPage page)
    {
        _page = page;
    }

    /// <summary>
    /// Navigates to the forgot password page and waits for it to load.
    /// </summary>
    public async Task NavigateAsync()
    {
        await _page.GotoAsync("/forgot-password");
        await _page.WaitForInteractiveAsync();
        await Expect(PageTitle).ToBeVisibleAsync();
    }

    /// <summary>
    /// Waits for the page to load.
    /// </summary>
    public async Task WaitForPageAsync(int timeoutMs = 10000)
    {
        // Wait for the form's input to be visible
        await _page.Locator("input.mud-input-slot").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = timeoutMs
        });
    }

    /// <summary>
    /// Fills the email field.
    /// </summary>
    public async Task FillEmailAsync(string email)
    {
        // MudTextField renders with .mud-input-slot class on the actual input
        // Click first to focus the field, then type (more reliable for MudBlazor)
        var input = _page.Locator("input.mud-input-slot");
        await input.ClickAsync();
        await input.FillAsync(email);
    }

    /// <summary>
    /// Clicks the submit button.
    /// </summary>
    public async Task SubmitAsync()
    {
        // Use role-based locator for button
        await _page.GetByRole(AriaRole.Button, new() { Name = "Send Reset Link" }).ClickAsync();
    }

    /// <summary>
    /// Requests a password reset for the given email.
    /// </summary>
    public async Task RequestResetAsync(string email)
    {
        await FillEmailAsync(email);
        await SubmitAsync();
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

    /// <summary>Clicks the "Sign In" link (an interactive page: the live circuit must be up first).</summary>
    public async Task ClickSignInLinkAsync()
    {
        await _page.WaitForInteractiveAsync();
        await _page.ClickAsync(SignInLink);
    }

    /// <summary>The page title.</summary>
    public ILocator PageTitle => _page.Locator(PageTitleSelector);

    /// <summary>The success alert shown after a reset is requested.</summary>
    public ILocator SuccessAlert => _page.Locator(SuccessAlertSelector);

    /// <summary>The error alert.</summary>
    public ILocator ErrorAlert => _page.Locator(ErrorAlertSelector);
}
