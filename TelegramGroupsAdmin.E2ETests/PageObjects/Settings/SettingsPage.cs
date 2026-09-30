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
    private const string SettingsSidebarHeadingSelector = ".mud-typography-h6:has-text('Settings')";
    private const string AccessDeniedAlertSelector = ".mud-alert-error";

    // Settings navigation links (in sidebar)
    private const string GeneralSettingsLinkSelector = "a[href='/settings/system/general']";
    private const string SecuritySettingsLink = "a[href='/settings/system/security']";
    private const string AdminAccountsLinkSelector = "a[href='/settings/system/accounts']";
    private const string LoggingSettingsLinkSelector = "a[href='/settings/system/logging']";
    private const string BackgroundJobsLink = "a[href='/settings/system/jobs']";
    private const string ContentDetectionLinkSelector = "a[href='/settings/content-detection']";

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
        var accessDenied = _page.Locator(AccessDeniedAlertSelector);

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

        // Wait for the settings sidebar heading to confirm the page body has rendered past the auth gate,
        // or for the access denied alert. The sidebar "Settings" h6 is always present for any authorised
        // user (it lives outside the permission-gated nav links), so it is safe to wait on regardless of role.
        await Expect(SettingsSidebarHeading.Or(AccessDeniedAlert).First).ToBeVisibleAsync(new() { Timeout = 15000 });
    }

    /// <summary>
    /// The settings sidebar "Settings" heading. Present for any authorised user, outside the
    /// permission-gated nav links, so it is a safe render sync point regardless of role.
    /// </summary>
    public ILocator SettingsSidebarHeading => _page.Locator(SettingsSidebarHeadingSelector).First;

    /// <summary>
    /// The access denied / error alert. Its absence means the settings page loaded successfully.
    /// </summary>
    public ILocator AccessDeniedAlert => _page.Locator(AccessDeniedAlertSelector);

    /// <summary>
    /// The General settings nav link, representative of the infrastructure settings links (Owner only).
    /// </summary>
    public ILocator GeneralSettingsLink => _page.Locator(GeneralSettingsLinkSelector);

    /// <summary>
    /// The Logging settings nav link. Rendered for every role in the (default-expanded) System group,
    /// so it is a positive render sync for absence checks on the permission-gated System links.
    /// </summary>
    public ILocator LoggingSettingsLink => _page.Locator(LoggingSettingsLinkSelector);

    /// <summary>The content detection settings nav link.</summary>
    public ILocator ContentDetectionLink => _page.Locator(ContentDetectionLinkSelector);

    /// <summary>The admin accounts nav link.</summary>
    public ILocator AdminAccountsLink => _page.Locator(AdminAccountsLinkSelector);

    /// <summary>
    /// Gets the current URL path.
    /// </summary>
    public string GetCurrentPath()
    {
        var uri = new Uri(_page.Url);
        return uri.AbsolutePath;
    }
}
