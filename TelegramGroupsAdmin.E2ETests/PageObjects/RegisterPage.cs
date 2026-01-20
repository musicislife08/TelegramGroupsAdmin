using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.PageObjects;

/// <summary>
/// Page object for Register.razor (interactive Blazor page with MudBlazor components).
/// Uses label-based selectors since MudBlazor generates accessible form controls.
/// </summary>
public class RegisterPage
{
    private readonly IPage _page;

    // MudBlazor components use labels - Playwright's GetByLabel works well
    // MudAlert uses specific classes for severity
    private const string ErrorAlert = ".mud-alert-error, .mud-alert-filled-error";
    private const string SuccessAlert = ".mud-alert-success, .mud-alert-filled-success";
    private const string InfoAlert = ".mud-alert-info, .mud-alert-filled-info";
    private const string WarningAlert = ".mud-alert-warning, .mud-alert-filled-warning";
    private const string SignInLink = "a[href='/login']";
    private const string RestoreBackupButton = "button:has-text('Restore from Backup')";

    public RegisterPage(IPage page)
    {
        _page = page;
    }

    /// <summary>
    /// Navigates to the register page.
    /// </summary>
    public async Task NavigateAsync()
    {
        // NetworkIdle ensures SignalR connection is established for this interactive Blazor page
        await _page.GotoAsync("/register", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        // Wait for MudBlazor to fully render - wait for the Create Account button
        await _page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create Account" }).WaitForAsync();
    }

    /// <summary>
    /// Fills the invite code field (only visible when not first run).
    /// </summary>
    public async Task FillInviteCodeAsync(string inviteCode)
    {
        // Invite code is a text input - find by the label text in parent container
        var inviteInput = _page.Locator(".mud-input-control:has-text('Invite Code') input.mud-input-slot");
        await inviteInput.ClickAsync();
        await inviteInput.FillAsync(inviteCode);
    }

    /// <summary>
    /// Fills the email field.
    /// MudBlazor inputs use .mud-input-slot class. GetByLabel doesn't work because
    /// MudBlazor doesn't create proper for/id label associations.
    /// </summary>
    public async Task FillEmailAsync(string email)
    {
        // MudBlazor email field renders as input.mud-input-slot[type='email']
        var emailInput = _page.Locator("input.mud-input-slot[type='email']");
        await emailInput.ClickAsync();
        await emailInput.FillAsync(email);
    }

    /// <summary>
    /// Fills the password field identified by its label.
    /// </summary>
    public async Task FillPasswordAsync(string password)
    {
        // Find password input by its exact label text within the containing control
        // Using :has() with exact text match to distinguish from "Confirm Password"
        var passwordInput = _page.Locator(".mud-input-control:has(label.mud-input-label:text-is('Password')) input.mud-input-slot");
        await passwordInput.ClickAsync();
        await passwordInput.FillAsync(password);
    }

    /// <summary>
    /// Fills the confirm password field identified by its label.
    /// </summary>
    public async Task FillConfirmPasswordAsync(string confirmPassword)
    {
        // Find confirm password input by its label text
        var confirmInput = _page.Locator(".mud-input-control:has(label.mud-input-label:text-is('Confirm Password')) input.mud-input-slot");
        await confirmInput.ClickAsync();
        await confirmInput.FillAsync(confirmPassword);
    }

    /// <summary>
    /// Fills all registration fields.
    /// </summary>
    public async Task FillRegistrationFormAsync(string email, string password, string? inviteCode = null)
    {
        if (!string.IsNullOrEmpty(inviteCode))
        {
            await FillInviteCodeAsync(inviteCode);
        }
        await FillEmailAsync(email);
        await FillPasswordAsync(password);
        await FillConfirmPasswordAsync(password);
    }

    /// <summary>
    /// Clicks the Create Account button.
    /// </summary>
    public async Task SubmitAsync()
    {
        await _page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create Account" }).ClickAsync();
    }

    /// <summary>
    /// Performs complete registration: fill form and submit.
    /// </summary>
    public async Task RegisterAsync(string email, string password, string? inviteCode = null)
    {
        await FillRegistrationFormAsync(email, password, inviteCode);
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
    /// Waits for and returns the success message text.
    /// Returns null if no success message appears within the timeout.
    /// </summary>
    public async Task<string?> GetSuccessMessageAsync(int timeoutMs = 5000)
    {
        var successLocator = _page.Locator(SuccessAlert);

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
    /// Checks if error message is displayed.
    /// </summary>
    public async Task<bool> HasErrorMessageAsync()
    {
        return await _page.Locator(ErrorAlert).IsVisibleAsync();
    }

    /// <summary>
    /// Checks if success message is displayed.
    /// </summary>
    public async Task<bool> HasSuccessMessageAsync()
    {
        return await _page.Locator(SuccessAlert).IsVisibleAsync();
    }

    /// <summary>
    /// Checks if this is first-run mode (no invite code required).
    /// First run shows "Setup Owner Account" title.
    ///
    /// Uses Playwright's Expect() with auto-retry to handle Blazor async state changes.
    /// The page initially renders with default _isFirstRun=false, then OnInitializedAsync()
    /// updates the state causing a re-render with the correct title.
    /// </summary>
    public async Task<bool> IsFirstRunModeAsync()
    {
        try
        {
            await Expect(_page.GetByText("Setup Owner Account")).ToBeVisibleAsync();
            return true;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if the invite code field is visible.
    /// </summary>
    public async Task<bool> IsInviteCodeVisibleAsync()
    {
        // Check if the Invite Code label text is visible on the page
        return await _page.Locator(".mud-input-control:has-text('Invite Code')").IsVisibleAsync();
    }

    /// <summary>
    /// Checks if the restore backup button is visible (first-run only).
    /// Uses Playwright's Expect() with auto-retry for Blazor async rendering.
    /// </summary>
    public async Task<bool> IsRestoreBackupAvailableAsync()
    {
        try
        {
            await Expect(_page.Locator(RestoreBackupButton)).ToBeVisibleAsync();
            return true;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    /// <summary>
    /// Clicks the Sign In link to navigate to login.
    /// </summary>
    public async Task ClickSignInLinkAsync()
    {
        await _page.ClickAsync(SignInLink);
    }

    /// <summary>
    /// Waits for registration to complete and redirect.
    /// </summary>
    public async Task WaitForRedirectAsync(int timeoutMs = 10000)
    {
        await _page.WaitForURLAsync(url => !url.Contains("/register"), new PageWaitForURLOptions
        {
            Timeout = timeoutMs
        });
    }

    /// <summary>
    /// Waits for the loading spinner to appear (form submission started).
    /// </summary>
    public async Task WaitForLoadingAsync()
    {
        await _page.GetByText("Creating Account...").WaitForAsync(new LocatorWaitForOptions
        {
            Timeout = 5000
        });
    }

    /// <summary>
    /// Waits for the loading spinner to disappear (form submission complete).
    /// </summary>
    public async Task WaitForLoadingCompleteAsync(int timeoutMs = 10000)
    {
        await _page.GetByText("Creating Account...").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = timeoutMs
        });
    }
}
