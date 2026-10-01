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
    // MudBlazor 9 renders MudAlert severity as mud-alert-{variant}-{severity}; the default Text variant
    // gives .mud-alert-text-error (plain .mud-alert-error never matches). Settings.razor is
    // [Authorize(GlobalAdminOrOwner)], so it has no page-level access-denied alert: its only error alert
    // is the per-section one a non-Owner gets in place of an infrastructure section's body.
    private const string ErrorAlertSelector = ".mud-alert-text-error";
    private const string OwnerAccessRequiredText = "Owner access required for infrastructure settings";

    // Settings navigation links (in sidebar)
    private const string GeneralSettingsLinkSelector = "a[href='/settings/system/general']";
    private const string AdminAccountsLinkSelector = "a[href='/settings/system/accounts']";
    private const string LoggingSettingsLinkSelector = "a[href='/settings/system/logging']";

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
        // Wait for either the section title or the Owner-access-required alert that replaces a section body.
        // Both render for a non-Owner on an infrastructure section, so take .First (WaitForAsync is strict).
        var pageContent = _page.Locator(PageTitle);

        await pageContent.Or(OwnerAccessRequiredAlert).First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        // Wait for loading indicator to disappear
        var loadingIndicator = _page.Locator(LoadingIndicator);
        await Expect(loadingIndicator).Not.ToBeVisibleAsync(new() { Timeout = 5000 });

        // Wait for the settings sidebar heading to confirm the page body has rendered past the auth gate,
        // or for the Owner-access-required alert. The sidebar "Settings" h6 is always present for any
        // authorised user (it lives outside the permission-gated nav links), so it is safe to wait on regardless of role.
        await Expect(SettingsSidebarHeading.Or(OwnerAccessRequiredAlert).First).ToBeVisibleAsync(new() { Timeout = 15000 });
    }

    /// <summary>
    /// The settings sidebar "Settings" heading. Present for any authorised user, outside the
    /// permission-gated nav links, so it is a safe render sync point regardless of role.
    /// </summary>
    public ILocator SettingsSidebarHeading => _page.Locator(SettingsSidebarHeadingSelector).First;

    /// <summary>
    /// The "Owner access required for infrastructure settings" alert that Settings.razor renders in place of an
    /// infrastructure section's body for a non-Owner (the default /settings section, General, is one). It is the
    /// page's only error alert: an unauthorised role never reaches the page at all (the Authorize policy redirects).
    /// </summary>
    public ILocator OwnerAccessRequiredAlert =>
        _page.Locator(ErrorAlertSelector).Filter(new() { HasText = OwnerAccessRequiredText });

    /// <summary>
    /// The General settings nav link, representative of the infrastructure settings links (Owner only).
    /// </summary>
    public ILocator GeneralSettingsLink => _page.Locator(GeneralSettingsLinkSelector);

    /// <summary>
    /// The Logging settings nav link. Rendered for every role in the (default-expanded) System group,
    /// so it is a positive render sync for absence checks on the permission-gated System links.
    /// </summary>
    public ILocator LoggingSettingsLink => _page.Locator(LoggingSettingsLinkSelector);

    /// <summary>The admin accounts nav link.</summary>
    public ILocator AdminAccountsLink => _page.Locator(AdminAccountsLinkSelector);

}
