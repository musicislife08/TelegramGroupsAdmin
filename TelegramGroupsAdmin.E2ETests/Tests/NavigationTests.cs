using System.Text.RegularExpressions;
using Microsoft.Playwright;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests;

/// <summary>
/// Tests for basic navigation behavior.
/// These are the simplest E2E tests to validate the test infrastructure works.
/// </summary>
[TestFixture]
public class NavigationTests : E2ETestBase
{
    private RegisterPage _registerPage = null!;

    [SetUp]
    public void SetUp()
    {
        _registerPage = new RegisterPage(Page);
    }

    /// <summary>
    /// Waits for page to stabilize after navigation/redirect.
    /// NetworkIdle ensures SignalR connection is established for Blazor Server.
    /// </summary>
    private async Task WaitForPageStableAsync()
    {
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>
    /// Waits until the URL contains the expected path using Playwright's auto-retry.
    /// Uses Expect() which automatically retries until timeout.
    /// </summary>
    private async Task WaitUntilUrlContainsAsync(string pathFragment, int timeoutMs = 10000)
    {
        await Expect(Page).ToHaveURLAsync(
            new Regex($".*{Regex.Escape(pathFragment)}.*"),
            new() { Timeout = timeoutMs });
    }

    [Test]
    public async Task EmptyDatabase_NavigateToLogin_RedirectsToRegister()
    {
        // Arrange - database is empty (no users exist)
        // This is the default state for a fresh test database

        // Act - navigate to login page
        await NavigateToAsync("/login");
        await WaitForPageStableAsync();

        // Assert - should redirect to register page (first-run mode)
        // The app redirects to /register when no users exist
        await WaitUntilUrlContainsAsync("/register");

        // Wait for MudBlazor to render the page content
        await Page.WaitForSelectorAsync("text=Setup Owner Account", new PageWaitForSelectorOptions
        {
            Timeout = 5000
        });

        // Verify first-run mode is active (no invite code required)
        Assert.That(await _registerPage.IsFirstRunModeAsync(), Is.True,
            "Register page should show 'Setup Owner Account' in first-run mode");
    }

    [Test]
    public async Task EmptyDatabase_NavigateToRoot_RedirectsToRegister()
    {
        // Arrange - database is empty

        // Act - navigate to root
        await NavigateToAsync("/");
        await WaitForPageStableAsync();

        // Assert - should eventually end up at register
        // Root may redirect through login first, then to register
        await WaitUntilUrlContainsAsync("/register", timeoutMs: 15000);

        // Wait for MudBlazor to render the restore backup button
        await Page.WaitForSelectorAsync("text=Restore from Backup", new PageWaitForSelectorOptions
        {
            Timeout = 5000
        });

        // Verify first-run mode shows restore option
        Assert.That(await _registerPage.IsRestoreBackupAvailableAsync(), Is.True,
            "First-run mode should offer restore from backup option");
    }
}
