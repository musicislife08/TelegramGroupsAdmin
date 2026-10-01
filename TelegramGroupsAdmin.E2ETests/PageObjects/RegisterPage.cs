using Microsoft.Playwright;

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
    private const string ErrorAlertSelector = ".mud-alert-error, .mud-alert-filled-error";
    private const string SuccessAlertSelector = ".mud-alert-success, .mud-alert-filled-success";
    private const string InfoAlert = ".mud-alert-info, .mud-alert-filled-info";
    private const string WarningAlert = ".mud-alert-warning, .mud-alert-filled-warning";
    private const string SignInLink = "a[href='/login']";
    private const string RestoreBackupButtonSelector = "button:has-text('Restore from Backup')";

    public RegisterPage(IPage page)
    {
        _page = page;
    }

    /// <summary>
    /// Navigates to the register page.
    /// </summary>
    public async Task NavigateAsync()
    {
        // Interactive Blazor page: wait for the live circuit before interacting
        await _page.GotoAsync("/register");
        await _page.WaitForInteractiveAsync();
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

    /// <summary>The error alert.</summary>
    public ILocator ErrorAlert => _page.Locator(ErrorAlertSelector);

    /// <summary>The success alert.</summary>
    public ILocator SuccessAlert => _page.Locator(SuccessAlertSelector);

    /// <summary>
    /// The "Setup Owner Account" title, shown only in first-run mode (no invite code required).
    /// The page initially renders with default _isFirstRun=false, then OnInitializedAsync()
    /// updates the state causing a re-render with this title, so assert on it with a retrying Expect.
    /// </summary>
    public ILocator FirstRunTitle => _page.GetByText("Setup Owner Account");

    /// <summary>The invite code field (hidden in first-run mode).</summary>
    public ILocator InviteCodeField => _page.Locator(".mud-input-control:has-text('Invite Code')");

    /// <summary>The "Restore from Backup" button (first-run only).</summary>
    public ILocator RestoreBackupButton => _page.Locator(RestoreBackupButtonSelector);

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
