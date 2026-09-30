using Microsoft.Playwright;

namespace TelegramGroupsAdmin.E2ETests.PageObjects;

/// <summary>
/// Page object for ResendVerification.razor (/resend-verification).
/// Handles the resend verification email form (static SSR page with plain HTML form).
/// Note: Despite being in the Blazor app, this page uses [ExcludeFromInteractiveRouting]
/// so it renders as a pure server-side page with standard HTML form submission.
/// </summary>
public class ResendVerificationPage
{
    private readonly IPage _page;

    // Selectors - static SSR page with plain HTML (inside MudBlazor layout)
    private const string PageTitleSelector = ".title";
    private const string EmailInput = "input#email";
    private const string SubmitButton = "button[type='submit']";
    private const string ErrorAlertSelector = ".alert-error";
    private const string SuccessAlertSelector = ".alert-success";
    private const string BackToLoginLink = "a[href='/login']";

    public ResendVerificationPage(IPage page)
    {
        _page = page;
    }

    /// <summary>
    /// Navigates to the resend verification page.
    /// </summary>
    public async Task NavigateAsync()
    {
        await _page.GotoAsync("/resend-verification");
        await _page.WaitForSelectorAsync(EmailInput);
    }

    /// <summary>
    /// Waits for the page to load.
    /// </summary>
    public async Task WaitForPageAsync(int timeoutMs = 10000)
    {
        await _page.WaitForSelectorAsync(EmailInput, new PageWaitForSelectorOptions
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
        await _page.FillAsync(EmailInput, email);
    }

    /// <summary>
    /// Clicks the submit button.
    /// </summary>
    public async Task SubmitAsync()
    {
        await _page.ClickAsync(SubmitButton);
    }

    /// <summary>
    /// Requests to resend verification email.
    /// </summary>
    public async Task RequestResendAsync(string email)
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

    /// <summary>The page title.</summary>
    public ILocator PageTitle => _page.Locator(PageTitleSelector);

    /// <summary>The success alert shown after a resend is requested.</summary>
    public ILocator SuccessAlert => _page.Locator(SuccessAlertSelector);

    /// <summary>The error alert (a generic response that does not reveal whether the email exists).</summary>
    public ILocator ErrorAlert => _page.Locator(ErrorAlertSelector);

    /// <summary>
    /// Clicks the back to login link.
    /// </summary>
    public async Task ClickBackToLoginAsync()
    {
        await _page.ClickAsync(BackToLoginLink);
    }
}
