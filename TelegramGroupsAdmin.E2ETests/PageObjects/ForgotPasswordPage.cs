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
    private const string PageTitle = ".mud-typography-h4";
    private const string ErrorAlert = ".mud-alert-text-error";
    private const string SuccessAlert = ".mud-alert-text-success";
    private const string SignInLink = "a[href='/login']";
    private const string LoadingIndicator = ".mud-progress-circular";

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
        // Wait for the page title to be visible (indicates Blazor Server hydrated)
        await Expect(_page.Locator(PageTitle)).ToBeVisibleAsync();
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
    /// Gets the error alert locator for Expect assertions.
    /// </summary>
    public ILocator ErrorAlertLocator => _page.Locator(ErrorAlert);

    /// <summary>
    /// Gets the error message text.
    /// </summary>
    public async Task<string?> GetErrorMessageAsync()
    {
        var errorLocator = _page.Locator(ErrorAlert);
        if (!await errorLocator.IsVisibleAsync())
            return null;

        return await errorLocator.TextContentAsync();
    }

    /// <summary>
    /// Gets the page title text.
    /// </summary>
    public async Task<string?> GetPageTitleAsync()
    {
        return await _page.Locator(PageTitle).TextContentAsync();
    }

    /// <summary>
    /// Checks if the form is in loading state.
    /// </summary>
    public async Task<bool> IsLoadingAsync()
    {
        return await _page.Locator(LoadingIndicator).IsVisibleAsync();
    }
}
