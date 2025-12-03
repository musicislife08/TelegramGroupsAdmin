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
    private const string PageTitle = ".title";
    private const string EmailInput = "input#email";
    private const string SubmitButton = "button[type='submit']";
    private const string ErrorAlert = ".alert-error";
    private const string SuccessAlert = ".alert-success";
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
        await _page.WaitForSelectorAsync(SuccessAlert, new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = timeoutMs
        });
    }

    /// <summary>
    /// Checks if a success message is displayed.
    /// </summary>
    public async Task<bool> HasSuccessMessageAsync()
    {
        return await _page.Locator(SuccessAlert).IsVisibleAsync();
    }

    /// <summary>
    /// Gets the success message text.
    /// </summary>
    public async Task<string?> GetSuccessMessageAsync()
    {
        var successLocator = _page.Locator(SuccessAlert);
        if (!await successLocator.IsVisibleAsync())
            return null;

        return await successLocator.TextContentAsync();
    }

    /// <summary>
    /// Checks if an error message is displayed.
    /// </summary>
    public async Task<bool> HasErrorMessageAsync()
    {
        return await _page.Locator(ErrorAlert).IsVisibleAsync();
    }

    /// <summary>
    /// Gets the error message text.
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
    /// Gets the page title text.
    /// </summary>
    public async Task<string?> GetPageTitleAsync()
    {
        return await _page.Locator(PageTitle).TextContentAsync();
    }

    /// <summary>
    /// Clicks the back to login link.
    /// </summary>
    public async Task ClickBackToLoginAsync()
    {
        await _page.ClickAsync(BackToLoginLink);
    }
}
