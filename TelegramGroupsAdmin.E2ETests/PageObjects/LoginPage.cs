using Microsoft.Playwright;

namespace TelegramGroupsAdmin.E2ETests.PageObjects;

/// <summary>
/// Page object for Login.razor (static SSR page with plain HTML forms).
/// Encapsulates selectors and common operations for login page interactions.
/// </summary>
public class LoginPage
{
    private readonly IPage _page;

    // Selectors - Login.razor uses plain HTML inputs (static SSR)
    private const string EmailInput = "input#email";
    private const string PasswordInput = "input#password";
    private const string SubmitButton = "button[type='submit']";
    private const string ErrorAlert = ".alert-error";
    private const string SuccessAlert = ".alert-success";
    private const string ForgotPasswordLink = "a[href='/forgot-password']";
    private const string ResendVerificationLink = "a[href='/resend-verification']";

    public LoginPage(IPage page)
    {
        _page = page;
    }

    /// <summary>
    /// Navigates to the login page.
    /// </summary>
    public async Task NavigateAsync()
    {
        await _page.GotoAsync("/login");
        await _page.WaitForSelectorAsync(EmailInput);
    }

    /// <summary>
    /// Fills in email and password fields.
    /// </summary>
    public async Task FillCredentialsAsync(string email, string password)
    {
        await _page.FillAsync(EmailInput, email);
        await _page.FillAsync(PasswordInput, password);
    }

    /// <summary>
    /// Clicks the submit button.
    /// </summary>
    public async Task SubmitAsync()
    {
        await _page.ClickAsync(SubmitButton);
    }

    /// <summary>
    /// Performs a complete login flow: fill credentials and submit.
    /// </summary>
    public async Task LoginAsync(string email, string password)
    {
        await FillCredentialsAsync(email, password);
        await SubmitAsync();
    }

    /// <summary>
    /// Waits for and returns the error message text.
    /// Returns null if no error message appears within the timeout.
    /// </summary>
    public async Task<string?> GetErrorMessageAsync(int timeoutMs = 5000)
    {
        var errorLocator = _page.Locator(ErrorAlert);

        // Use WaitForAsync with visible state for proper waiting
        // Timeout throws PlaywrightException which we catch and return null
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
    /// Waits for and returns the success message text.
    /// Returns null if no success message appears within the timeout.
    /// </summary>
    public async Task<string?> GetSuccessMessageAsync(int timeoutMs = 5000)
    {
        var successLocator = _page.Locator(SuccessAlert);

        // Use WaitForAsync with visible state for proper waiting
        // Timeout throws PlaywrightException which we catch and return null
        try
        {
            await successLocator.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = timeoutMs
            });
            return await successLocator.TextContentAsync();
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
    /// Checks if a success message is displayed.
    /// </summary>
    public async Task<bool> HasSuccessMessageAsync()
    {
        return await _page.Locator(SuccessAlert).IsVisibleAsync();
    }

    /// <summary>
    /// Checks if the forgot password link is visible (indicates email is configured).
    /// </summary>
    public async Task<bool> IsForgotPasswordAvailableAsync()
    {
        return await _page.Locator(ForgotPasswordLink).IsVisibleAsync();
    }

    /// <summary>
    /// Clicks the forgot password link.
    /// </summary>
    public async Task ClickForgotPasswordAsync()
    {
        await _page.ClickAsync(ForgotPasswordLink);
    }

    /// <summary>
    /// Clicks the resend verification link.
    /// </summary>
    public async Task ClickResendVerificationAsync()
    {
        await _page.ClickAsync(ResendVerificationLink);
    }

    /// <summary>
    /// Waits for redirect away from login page (successful login).
    /// </summary>
    public async Task WaitForRedirectAsync(int timeoutMs = 10000)
    {
        await _page.WaitForURLAsync(url => !url.Contains("/login"), new PageWaitForURLOptions
        {
            Timeout = timeoutMs
        });
    }

    /// <summary>
    /// Waits for redirect to a specific URL pattern.
    /// </summary>
    public async Task WaitForUrlAsync(string urlPattern, int timeoutMs = 10000)
    {
        await _page.WaitForURLAsync(urlPattern, new PageWaitForURLOptions
        {
            Timeout = timeoutMs
        });
    }
}
