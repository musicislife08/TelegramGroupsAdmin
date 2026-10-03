using Microsoft.Playwright;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Settings;

/// <summary>
/// Tests for the Service Message Deletion Settings page.
/// Tests the configuration of which Telegram service messages are automatically deleted.
/// Accessible by GlobalAdmin or Owner.
/// </summary>
[TestFixture]
public class ServiceMessageSettingsTests : AuthenticatedTestBase
{
    private SettingsPage _settingsPage = null!;

    [SetUp]
    public void SetUp()
    {
        _settingsPage = new SettingsPage(Page);
    }

    #region Page Load Tests

    [Test]
    public async Task ServiceMessages_PageLoads_ShowsAllToggles()
    {
        // Arrange
        await LoginAsGlobalAdminAsync();

        // Act
        await _settingsPage.NavigateToServiceMessagesAsync();

        // Assert - all 6 toggles should be visible
        await _settingsPage.ExpectAllServiceMessageTogglesVisibleAsync();

        // Assert - exactly 6 toggle switches for service message types
        await Expect(_settingsPage.ServiceMessageToggles).ToHaveCountAsync(6);
    }

    [Test]
    public async Task ServiceMessages_PageLoads_ShowsPageTitle()
    {
        // Arrange
        await LoginAsGlobalAdminAsync();

        // Act
        await _settingsPage.NavigateToServiceMessagesAsync();

        // Assert
        await Expect(Page.GetByText("Service Message Deletion", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText("Configure which types of Telegram service messages are automatically deleted")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ServiceMessages_PageLoads_ShowsSaveAndResetButtons()
    {
        // Arrange
        await LoginAsGlobalAdminAsync();

        // Act
        await _settingsPage.NavigateToServiceMessagesAsync();

        // Assert
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save Configuration" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Reset to Defaults" })).ToBeVisibleAsync();
    }

    #endregion

    #region Save Configuration Tests

    [Test]
    public async Task ServiceMessages_SaveConfiguration_ShowsSuccessSnackbar()
    {
        // Arrange
        await LoginAsOwnerAsync();
        await _settingsPage.NavigateToServiceMessagesAsync();

        // Act - click save
        await _settingsPage.ClickSaveServiceMessagesAsync();

        // Assert - snackbar confirms save
        await Expect(Page.Locator(".mud-snackbar")).ToBeVisibleAsync();
        await Expect(Page.Locator(".mud-snackbar")).ToContainTextAsync("saved", new() { IgnoreCase = true });
    }

    [Test]
    public async Task ServiceMessages_ToggleAndSave_PersistsChanges()
    {
        // Arrange
        await LoginAsOwnerAsync();
        await _settingsPage.NavigateToServiceMessagesAsync();

        // Get initial state of Photo Changes (wait for the switch to render first)
        var photoToggleInput = _settingsPage.ServiceMessageToggleInput("Delete Photo Changes");
        await Expect(_settingsPage.ServiceMessageToggle("Delete Photo Changes")).ToBeVisibleAsync();
#pragma warning disable RS0030 // Initial state is needed to assert the persisted value is the opposite
        var initialState = await photoToggleInput.IsCheckedAsync();
#pragma warning restore RS0030

        // Act - toggle and save
        await _settingsPage.ToggleServiceMessageDeletionAsync("Delete Photo Changes");
        await _settingsPage.ClickSaveServiceMessagesAsync();

        // Wait for save confirmation
        await Expect(Page.Locator(".mud-snackbar")).ToContainTextAsync("saved", new() { IgnoreCase = true });

        // Reload page to verify persistence
        await _settingsPage.NavigateToServiceMessagesAsync();

        // Assert - toggled state should persist after page reload
        await Expect(photoToggleInput).ToBeCheckedAsync(new() { Checked = !initialState });

        // Cleanup - toggle back and save
        await _settingsPage.ToggleServiceMessageDeletionAsync("Delete Photo Changes");
        await _settingsPage.ClickSaveServiceMessagesAsync();
        await Expect(Page.Locator(".mud-snackbar")).ToBeVisibleAsync();
    }

    #endregion

    #region Reset to Defaults Tests

    [Test]
    public async Task ServiceMessages_ResetToDefaults_ShowsInfoSnackbar()
    {
        // Arrange
        await LoginAsOwnerAsync();
        await _settingsPage.NavigateToServiceMessagesAsync();

        // Act - click reset
        await Page.GetByRole(AriaRole.Button, new() { Name = "Reset to Defaults" }).ClickAsync();

        // Assert - snackbar shows info
        await Expect(Page.Locator(".mud-snackbar")).ToBeVisibleAsync();
        await Expect(Page.Locator(".mud-snackbar")).ToContainTextAsync("Defaults loaded", new() { IgnoreCase = true });
    }

    [Test]
    public async Task ServiceMessages_ResetToDefaults_AllTogglesEnabled()
    {
        // Arrange
        await LoginAsOwnerAsync();
        await _settingsPage.NavigateToServiceMessagesAsync();

        // Act - reset to defaults
        await Page.GetByRole(AriaRole.Button, new() { Name = "Reset to Defaults" }).ClickAsync();

        // Assert - all toggles should be enabled (default is true for all)
        await Expect(_settingsPage.ServiceMessageToggleInput("Delete Join Messages")).ToBeCheckedAsync();
        await Expect(_settingsPage.ServiceMessageToggleInput("Delete Leave Messages")).ToBeCheckedAsync();
        await Expect(_settingsPage.ServiceMessageToggleInput("Delete Photo Changes")).ToBeCheckedAsync();
        await Expect(_settingsPage.ServiceMessageToggleInput("Delete Title Changes")).ToBeCheckedAsync();
        await Expect(_settingsPage.ServiceMessageToggleInput("Delete Pin Notifications")).ToBeCheckedAsync();
        await Expect(_settingsPage.ServiceMessageToggleInput("Delete Chat Creation Messages")).ToBeCheckedAsync();
    }

    #endregion

    #region Navigation Tests

    [Test]
    public async Task ServiceMessages_NavLink_AppearsInTelegramSection()
    {
        // Arrange
        await LoginAsGlobalAdminAsync();
        await _settingsPage.NavigateAsync();

        // Act - expand Telegram group by clicking on it
        var telegramGroup = Page.Locator(".mud-nav-group:has-text('Telegram')");
        await telegramGroup.ClickAsync();

        // Assert - Service Messages link should be visible
        await Expect(Page.Locator("a[href='/settings/telegram/service-messages']")).ToBeVisibleAsync();
    }

    #endregion

    #region Help Text Tests

    [Test]
    public async Task ServiceMessages_ShowsDescriptiveHelpText()
    {
        // Arrange
        await LoginAsGlobalAdminAsync();

        // Act
        await _settingsPage.NavigateToServiceMessagesAsync();

        // Assert - help text for each toggle should be visible
        await Expect(Page.GetByText("\"User joined the group\" notifications")).ToBeVisibleAsync();
        await Expect(Page.GetByText("\"User left the group\" notifications")).ToBeVisibleAsync();
        await Expect(Page.GetByText("Group photo added/removed notifications")).ToBeVisibleAsync();
        await Expect(Page.GetByText("\"User changed the group title\" notifications")).ToBeVisibleAsync();
        await Expect(Page.GetByText("\"User pinned a message\" notifications")).ToBeVisibleAsync();
        await Expect(Page.GetByText("Group/supergroup/channel created notifications")).ToBeVisibleAsync();
    }

    #endregion
}
