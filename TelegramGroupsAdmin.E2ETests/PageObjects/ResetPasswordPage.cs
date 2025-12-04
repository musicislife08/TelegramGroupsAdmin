using Microsoft.Playwright;

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
    private const string ErrorAlert = ".mud-alert-text-error";
    private const string SuccessAlert = ".mud-alert-text-success";
    private const string SignInLink = "a[href='/login']";
    private const string RequestNewLinkButton = "a[href='/forgot-password']";
    private const string LoadingIndicator = ".mud-progress-circular";

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
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>
    /// Navigates directly from a reset link URL.
    /// </summary>
    public async Task NavigateFromLinkAsync(string resetLink)
    {
        // Extract the path from the full URL
        var uri = new Uri(resetLink);
        await _page.GotoAsync($"{uri.PathAndQuery}");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
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
    /// Gets locator for the first password input (New Password).
    /// </summary>
    private ILocator NewPasswordInput => _page.Locator("input.mud-input-slot").First;

    /// <summary>
    /// Gets locator for the second password input (Confirm Password).
    /// </summary>
    private ILocator ConfirmPasswordInput => _page.Locator("input.mud-input-slot").Nth(1);

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
    /// Checks if the password form is visible (indicates valid token).
    /// </summary>
    public async Task<bool> IsFormVisibleAsync()
    {
        return await NewPasswordInput.IsVisibleAsync();
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
        await _page.WaitForSelectorAsync(SuccessAlert, new PageWaitForSelectorOptions
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
    /// Checks if the "Request New Link" button is visible (indicates invalid/missing token).
    /// </summary>
    public async Task<bool> IsRequestNewLinkVisibleAsync()
    {
        return await _page.Locator(RequestNewLinkButton).IsVisibleAsync();
    }

    /// <summary>
    /// Checks if the form is in loading state.
    /// </summary>
    public async Task<bool> IsLoadingAsync()
    {
        return await _page.Locator(LoadingIndicator).IsVisibleAsync();
    }
}
