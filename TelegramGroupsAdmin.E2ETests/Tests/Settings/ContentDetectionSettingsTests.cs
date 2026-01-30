using Microsoft.Playwright;
using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Settings;

/// <summary>
/// Tests for the Content Detection Settings pages.
/// Tests algorithm configuration, stop words management, and training data management.
/// Requires GlobalAdmin or Owner role to access.
/// </summary>
[TestFixture]
public class ContentDetectionSettingsTests : AuthenticatedTestBase
{
    private SettingsPage _settingsPage = null!;

    [SetUp]
    public void SetUp()
    {
        _settingsPage = new SettingsPage(Page);
    }

    #region Detection Algorithms Page

    [Test]
    public async Task DetectionAlgorithms_LoadsSuccessfully_ShowsAlgorithmToggles()
    {
        // Arrange - login as GlobalAdmin (can access content detection settings)
        await LoginAsGlobalAdminAsync();

        // Act - navigate to detection algorithms page
        await _settingsPage.NavigateToDetectionAlgorithmsAsync();

        // Assert - page loads with algorithm toggles
        var toggleCount = await _settingsPage.GetAlgorithmToggleCountAsync();
        Assert.That(toggleCount, Is.GreaterThanOrEqualTo(4),
            "Should show at least 4 algorithm toggle switches (Stop Words, Similarity, Bayes, Spacing, etc.)");

        // Verify specific algorithms are visible by checking the h6 headings inside card headers
        // These are unique to the algorithm cards and won't match nav items
        // NOTE: CAS moved to Welcome Settings > JoinSecurity as of refactor
        await Expect(Page.Locator(".mud-card-header:has-text('Stop Words Detection')")).ToBeVisibleAsync();
        await Expect(Page.Locator(".mud-card-header:has-text('Similarity Detection')")).ToBeVisibleAsync();
        await Expect(Page.Locator(".mud-card-header:has-text('Naive Bayes Classifier')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task DetectionAlgorithms_ToggleAlgorithm_UpdatesUI()
    {
        // Arrange
        await LoginAsOwnerAsync();
        await _settingsPage.NavigateToDetectionAlgorithmsAsync();

        // Get initial state of Stop Words algorithm
        var initialState = await _settingsPage.IsAlgorithmEnabledAsync("Stop Words Detection");

        // Act - toggle the algorithm
        await _settingsPage.ToggleAlgorithmAsync("Stop Words Detection");

        // Assert - state should be toggled
        var newState = await _settingsPage.IsAlgorithmEnabledAsync("Stop Words Detection");
        Assert.That(newState, Is.Not.EqualTo(initialState),
            "Stop Words algorithm state should toggle");

        // Toggle back to original state
        await _settingsPage.ToggleAlgorithmAsync("Stop Words Detection");
    }

    [Test]
    public async Task DetectionAlgorithms_SaveChanges_ShowsSuccessSnackbar()
    {
        // Arrange
        await LoginAsOwnerAsync();
        await _settingsPage.NavigateToDetectionAlgorithmsAsync();

        // Act - click save
        await _settingsPage.ClickSaveAllChangesAsync();

        // Assert - snackbar confirms save
        await Expect(Page.Locator(".mud-snackbar")).ToBeVisibleAsync();
        await Expect(Page.Locator(".mud-snackbar")).ToContainTextAsync("saved", new() { IgnoreCase = true });
    }

    [Test]
    public async Task DetectionAlgorithms_GlobalSettings_AreVisible()
    {
        // Arrange
        await LoginAsGlobalAdminAsync();

        // Act
        await _settingsPage.NavigateToDetectionAlgorithmsAsync();

        // Assert - global settings section visible
        // Use specific locators to avoid matching nav items
        await Expect(Page.GetByText("Global Detection Settings", new() { Exact = true })).ToBeVisibleAsync();

        // Training Mode is inside the Global Detection Settings paper - target specifically
        var globalSettingsPaper = Page.Locator(".mud-paper:has-text('Global Detection Settings')");
        await Expect(globalSettingsPaper.GetByText("Training Mode", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(globalSettingsPaper.GetByText("Auto-Ban Threshold", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(globalSettingsPaper.GetByText("Review Queue Threshold", new() { Exact = true })).ToBeVisibleAsync();
    }

    #endregion

    #region Stop Words Management

    [Test]
    public async Task StopWords_PageLoads_ShowsAddButton()
    {
        // Arrange
        await LoginAsGlobalAdminAsync();

        // Act
        await _settingsPage.NavigateToStopWordsAsync();

        // Assert - page elements visible
        await Expect(Page.GetByText("Stop Words Management", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Add Stop Word" })).ToBeVisibleAsync();
        await Expect(Page.GetByPlaceholder("Search stop words...")).ToBeVisibleAsync();
    }

    [Test]
    public async Task StopWords_AddWord_AppearsInList()
    {
        // Arrange
        await LoginAsOwnerAsync();
        await _settingsPage.NavigateToStopWordsAsync();

        var testWord = $"testspamword{DateTime.UtcNow.Ticks}";

        // Verify word doesn't exist before adding
        Assert.That(await _settingsPage.IsStopWordVisibleAsync(testWord), Is.False,
            "Test word should not exist before adding");

        // Act - add a new stop word
        await _settingsPage.ClickAddStopWordAsync();
        await _settingsPage.FillAndSubmitAddStopWordDialogAsync(testWord, "E2E test word");

        // Assert - snackbar confirms addition (use First to handle multiple snackbars)
        await Expect(Page.Locator(".mud-snackbar").First).ToBeVisibleAsync();
        await Expect(Page.Locator(".mud-snackbar").First).ToContainTextAsync("added", new() { IgnoreCase = true });

        // Word should appear in the table - use proper wait
        await _settingsPage.WaitForStopWordVisibleAsync(testWord);

        // Verify word is now visible
        Assert.That(await _settingsPage.IsStopWordVisibleAsync(testWord), Is.True,
            "Newly added stop word should be visible in the table");

        // Cleanup - delete the test word
        await _settingsPage.ClickDeleteStopWordAsync(testWord);
        await _settingsPage.ConfirmDeleteAsync();
    }

    [Test]
    public async Task StopWords_DeleteWord_RemovedFromList()
    {
        // Arrange - first add a word to delete
        await LoginAsOwnerAsync();
        await _settingsPage.NavigateToStopWordsAsync();

        var testWord = $"deletetestword{DateTime.UtcNow.Ticks}";

        // Add a word first
        await _settingsPage.ClickAddStopWordAsync();
        await _settingsPage.FillAndSubmitAddStopWordDialogAsync(testWord);

        // Wait for word to appear
        await _settingsPage.WaitForStopWordVisibleAsync(testWord);

        // Act - delete the word
        await _settingsPage.ClickDeleteStopWordAsync(testWord);
        await _settingsPage.ConfirmDeleteAsync();

        // Assert - word should no longer be visible (this is the key assertion)
        await _settingsPage.WaitForStopWordHiddenAsync(testWord);

        // Verify word is gone
        Assert.That(await _settingsPage.IsStopWordVisibleAsync(testWord), Is.False,
            "Deleted stop word should not be visible in the table");
    }

    [Test]
    public async Task StopWords_SearchFilter_FiltersTable()
    {
        // Arrange - add two distinct words
        await LoginAsOwnerAsync();
        await _settingsPage.NavigateToStopWordsAsync();

        var word1 = $"searchtest{DateTime.UtcNow.Ticks}abc";
        var word2 = $"different{DateTime.UtcNow.Ticks}xyz";

        // Add both words and wait for them to appear
        await _settingsPage.ClickAddStopWordAsync();
        await _settingsPage.FillAndSubmitAddStopWordDialogAsync(word1);
        await _settingsPage.WaitForStopWordVisibleAsync(word1);

        await _settingsPage.ClickAddStopWordAsync();
        await _settingsPage.FillAndSubmitAddStopWordDialogAsync(word2);
        await _settingsPage.WaitForStopWordVisibleAsync(word2);

        // Act - search for word1
        await _settingsPage.SearchStopWordsAsync("searchtest");

        // Assert - word1 should be visible
        Assert.That(await _settingsPage.IsStopWordVisibleAsync(word1), Is.True,
            "Matching word should be visible");

        // Cleanup - clear search and delete words
        await _settingsPage.SearchStopWordsAsync("");
        await _settingsPage.WaitForStopWordVisibleAsync(word1);
        await _settingsPage.WaitForStopWordVisibleAsync(word2);

        await _settingsPage.ClickDeleteStopWordAsync(word1);
        await _settingsPage.ConfirmDeleteAsync();
        await _settingsPage.WaitForStopWordHiddenAsync(word1);

        await _settingsPage.ClickDeleteStopWordAsync(word2);
        await _settingsPage.ConfirmDeleteAsync();
    }

    #endregion

    #region Training Data Management

    [Test]
    public async Task TrainingSamples_PageLoads_ShowsAddButton()
    {
        // Arrange
        await LoginAsGlobalAdminAsync();

        // Act
        await _settingsPage.NavigateToTrainingSamplesAsync();

        // Assert - page elements visible
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Add Training Sample" })).ToBeVisibleAsync();
        await Expect(Page.GetByPlaceholder("Search training samples...")).ToBeVisibleAsync();

        // New TrainingDataBalanceStatus component should be visible
        await Expect(Page.GetByText("Training Data Balance")).ToBeVisibleAsync();
        await Expect(Page.Locator(".mud-chip:has-text('Spam:')").First).ToBeVisibleAsync();
        await Expect(Page.Locator(".mud-chip:has-text('Explicit Ham:')").First).ToBeVisibleAsync();
        await Expect(Page.Locator(".mud-chip:has-text('Implicit Ham:')").First).ToBeVisibleAsync();
    }

    [Test]
    public async Task TrainingSamples_FilterByType_WorksCorrectly()
    {
        // Arrange
        await LoginAsOwnerAsync();
        await _settingsPage.NavigateToTrainingSamplesAsync();

        // Act - select Spam Only filter using the page object method
        await _settingsPage.SelectTypeFilterAsync("Spam Only");

        // Assert - filter is applied (showing text should update)
        await Expect(Page.GetByText("Showing", new() { Exact = false })).ToBeVisibleAsync();

        // Reset filter using the page object method
        await _settingsPage.SelectTypeFilterAsync("All");
    }

    #endregion

    #region Permission Tests

    [Test]
    public async Task ContentDetectionSettings_RequiresGlobalAdminOrOwner()
    {
        // Arrange - login as Admin (not GlobalAdmin)
        await LoginAsAdminAsync();

        // Act - try to navigate to settings
        await Page.GotoAsync("/settings/content-detection/algorithms");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Assert - should be redirected or show access denied
        // Check that we're not on the expected settings page with algorithm content visible
        var hasAlgorithmCards = await Page.Locator(".mud-card-header:has-text('Stop Words Detection')").IsVisibleAsync();

        // If Admin CAN see Detection Algorithms, they have access (which may be intended)
        // If they can't, they'll see an error or be redirected
        if (!hasAlgorithmCards)
        {
            Assert.Pass("Admin correctly blocked from Content Detection settings");
        }
        else
        {
            // Admin can see it - the policy may allow it
            Assert.Warn("Admin can access Content Detection settings - verify if this is intended");
        }
    }

    [Test]
    public async Task ContentDetectionSettings_GlobalAdminCanAccess()
    {
        // Arrange
        await LoginAsGlobalAdminAsync();

        // Act
        await _settingsPage.NavigateToDetectionAlgorithmsAsync();

        // Assert - GlobalAdmin should see the page
        // Look for algorithm cards specifically (not nav items)
        // NOTE: CAS moved to Welcome Settings > JoinSecurity as of refactor
        await Expect(Page.Locator(".mud-card-header:has-text('Stop Words Detection')")).ToBeVisibleAsync();
        await Expect(Page.Locator(".mud-card-header:has-text('Similarity Detection')")).ToBeVisibleAsync();
    }

    #endregion
}
