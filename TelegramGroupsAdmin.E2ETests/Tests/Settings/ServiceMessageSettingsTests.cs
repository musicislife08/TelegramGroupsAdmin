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
        Assert.That(await _settingsPage.AreAllServiceMessageTogglesVisibleAsync(), Is.True,
            "All 6 service message deletion toggles should be visible");

        var toggleCount = await _settingsPage.GetServiceMessageToggleCountAsync();
        Assert.That(toggleCount, Is.EqualTo(6),
            "Should show exactly 6 toggle switches for service message types");
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
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save Configuration" }).ClickAsync();

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

        // Get initial state of Photo Changes
        var initialState = await _settingsPage.IsServiceMessageDeletionEnabledAsync("Delete Photo Changes");

        // Act - toggle and save
        await _settingsPage.ToggleServiceMessageDeletionAsync("Delete Photo Changes");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save Configuration" }).ClickAsync();

        // Wait for save confirmation
        await Expect(Page.Locator(".mud-snackbar")).ToContainTextAsync("saved", new() { IgnoreCase = true });

        // Reload page to verify persistence
        await _settingsPage.NavigateToServiceMessagesAsync();

        // Assert - state should be persisted
        var persistedState = await _settingsPage.IsServiceMessageDeletionEnabledAsync("Delete Photo Changes");
        Assert.That(persistedState, Is.Not.EqualTo(initialState),
            "Toggled state should persist after page reload");

        // Cleanup - toggle back and save
        await _settingsPage.ToggleServiceMessageDeletionAsync("Delete Photo Changes");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save Configuration" }).ClickAsync();
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
        Assert.That(await _settingsPage.IsServiceMessageDeletionEnabledAsync("Delete Join Messages"), Is.True,
            "Delete Join Messages should be enabled by default");
        Assert.That(await _settingsPage.IsServiceMessageDeletionEnabledAsync("Delete Leave Messages"), Is.True,
            "Delete Leave Messages should be enabled by default");
        Assert.That(await _settingsPage.IsServiceMessageDeletionEnabledAsync("Delete Photo Changes"), Is.True,
            "Delete Photo Changes should be enabled by default");
        Assert.That(await _settingsPage.IsServiceMessageDeletionEnabledAsync("Delete Title Changes"), Is.True,
            "Delete Title Changes should be enabled by default");
        Assert.That(await _settingsPage.IsServiceMessageDeletionEnabledAsync("Delete Pin Notifications"), Is.True,
            "Delete Pin Notifications should be enabled by default");
        Assert.That(await _settingsPage.IsServiceMessageDeletionEnabledAsync("Delete Chat Creation Messages"), Is.True,
            "Delete Chat Creation Messages should be enabled by default");
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
