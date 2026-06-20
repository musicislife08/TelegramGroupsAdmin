using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.PageObjects.Settings;

/// <summary>
/// Page Object for the Settings page (/settings).
/// Provides methods to navigate settings sections and verify access control.
/// </summary>
public class SettingsPage
{
    private readonly IPage _page;

    // Selectors
    private const string PageTitle = ".mud-typography-h4";
    private const string LoadingIndicator = ".mud-progress-linear";
    private const string SettingsSidebarHeading = ".mud-typography-h6:has-text('Settings')";
    private const string AccessDeniedAlert = ".mud-alert-error";

    // Settings navigation links (in sidebar)
    private const string GeneralSettingsLink = "a[href='/settings/system/general']";
    private const string SecuritySettingsLink = "a[href='/settings/system/security']";
    private const string AdminAccountsLink = "a[href='/settings/system/accounts']";
    private const string BackgroundJobsLink = "a[href='/settings/system/jobs']";
    private const string ContentDetectionLink = "a[href='/settings/content-detection']";

    public SettingsPage(IPage page)
    {
        _page = page;
    }

    /// <summary>
    /// Navigates to the Settings page.
    /// </summary>
    public async Task NavigateAsync()
    {
        await _page.GotoAsync("/settings");
    }

    /// <summary>
    /// Waits for the page to finish loading.
    /// </summary>
    public async Task WaitForLoadAsync()
    {
        // Wait for either the page content or access denied message
        var pageContent = _page.Locator(PageTitle);
        var accessDenied = _page.Locator(AccessDeniedAlert);

        await pageContent.Or(accessDenied).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        // Wait for loading indicator to disappear
        var loadingIndicator = _page.Locator(LoadingIndicator);
        try
        {
            await loadingIndicator.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Hidden,
                Timeout = 5000
            });
        }
        catch (TimeoutException)
        {
            // Loading indicator may have already disappeared
        }

        // Wait for the settings sidebar heading to confirm the page body has rendered past the auth gate.
        // The sidebar "Settings" h6 is always present for any authorised user (it lives outside the
        // permission-gated nav links), so it is safe to wait on regardless of role.
        var isAccessDenied = await _page.Locator(AccessDeniedAlert).IsVisibleAsync();
        if (!isAccessDenied)
        {
            await Expect(_page.Locator(SettingsSidebarHeading).First).ToBeVisibleAsync(new() { Timeout = 15000 });
        }
    }

    /// <summary>
    /// Returns true if the settings page loaded successfully (not access denied).
    /// </summary>
    public async Task<bool> IsAccessAllowedAsync()
    {
        var accessDenied = _page.Locator(AccessDeniedAlert);
        var isAccessDenied = await accessDenied.IsVisibleAsync();
        return !isAccessDenied;
    }

    /// <summary>
    /// Returns true if access denied message is shown.
    /// </summary>
    public async Task<bool> IsAccessDeniedAsync()
    {
        var accessDenied = _page.Locator(AccessDeniedAlert);
        return await accessDenied.IsVisibleAsync();
    }

    /// <summary>
    /// Returns true if infrastructure settings links are visible (Owner only).
    /// Uses auto-retrying assertion to avoid races after the render gate.
    /// </summary>
    public async Task<bool> AreInfrastructureSettingsVisibleAsync()
    {
        var generalSettings = _page.Locator(GeneralSettingsLink);
        try
        {
            await Expect(generalSettings).ToBeVisibleAsync(new() { Timeout = 5000 });
            return true;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns true if content detection settings link is visible.
    /// </summary>
    public async Task<bool> IsContentDetectionVisibleAsync()
    {
        var contentDetection = _page.Locator(ContentDetectionLink);
        return await contentDetection.IsVisibleAsync();
    }

    /// <summary>
    /// Returns true if admin accounts link is visible.
    /// </summary>
    public async Task<bool> IsAdminAccountsVisibleAsync()
    {
        var adminAccounts = _page.Locator(AdminAccountsLink);
        return await adminAccounts.IsVisibleAsync();
    }

    /// <summary>
    /// Gets the current URL path.
    /// </summary>
    public string GetCurrentPath()
    {
        var uri = new Uri(_page.Url);
        return uri.AbsolutePath;
    }
}
