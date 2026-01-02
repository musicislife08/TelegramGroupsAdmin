using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.PageObjects;

/// <summary>
/// Page object for the Settings page (/settings).
/// Provides navigation and interactions for various settings sections.
/// </summary>
public class SettingsPage
{
    protected IPage Page { get; }
    private const string BasePath = "/settings";

    public SettingsPage(IPage page)
    {
        Page = page;
    }

    /// <summary>
    /// Navigates to the settings page.
    /// </summary>
    public async Task NavigateAsync()
    {
        await Page.GotoAsync(BasePath);
        await WaitForLoadAsync();
    }

    /// <summary>
    /// Waits for the page to finish loading.
    /// </summary>
    public async Task WaitForLoadAsync()
    {
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>
    /// Navigates to a specific section of the settings page.
    /// </summary>
    public async Task NavigateToSectionAsync(string section, string? subSection = null)
    {
        var url = subSection != null
            ? $"/settings/{section}/{subSection}"
            : $"/settings/{section}";
        await Page.GotoAsync(url);
        await WaitForLoadAsync();
    }

    /// <summary>
    /// Gets the page title displayed in the settings content area.
    /// </summary>
    public async Task<string> GetPageTitleAsync()
    {
        // The title is in a MudText element with Typo h4
        var title = Page.Locator("main .mud-text-h4, .mud-container h4, .mud-item h4").First;
        return await title.TextContentAsync() ?? string.Empty;
    }

    /// <summary>
    /// Checks if the settings nav menu is visible.
    /// </summary>
    public async Task<bool> IsNavMenuVisibleAsync()
    {
        var navMenu = Page.Locator(".mud-navmenu");
        return await navMenu.IsVisibleAsync();
    }

    #region Content Detection Section

    /// <summary>
    /// Navigates to the Detection Algorithms page.
    /// </summary>
    public async Task NavigateToDetectionAlgorithmsAsync()
    {
        await NavigateToSectionAsync("content-detection", "algorithms");
    }

    /// <summary>
    /// Navigates to the Stop Words Library page.
    /// </summary>
    public async Task NavigateToStopWordsAsync()
    {
        await NavigateToSectionAsync("training-data", "stopwords");
    }

    /// <summary>
    /// Navigates to the Training Samples page.
    /// </summary>
    public async Task NavigateToTrainingSamplesAsync()
    {
        await NavigateToSectionAsync("training-data", "samples");
    }

    /// <summary>
    /// Gets the count of visible algorithm toggle switches on the Detection Algorithms page.
    /// </summary>
    public async Task<int> GetAlgorithmToggleCountAsync()
    {
        // Algorithm toggles are MudSwitch elements in card headers
        var toggles = Page.Locator(".mud-card-header .mud-switch");
        return await toggles.CountAsync();
    }

    /// <summary>
    /// Checks if a specific algorithm is enabled by its name.
    /// </summary>
    public async Task<bool> IsAlgorithmEnabledAsync(string algorithmName)
    {
        // Find the card containing the algorithm name
        var card = Page.Locator($".mud-card:has-text('{algorithmName}')");
        var toggle = card.Locator(".mud-switch input");
        return await toggle.IsCheckedAsync();
    }

    /// <summary>
    /// Toggles an algorithm by its name.
    /// </summary>
    public async Task ToggleAlgorithmAsync(string algorithmName)
    {
        var card = Page.Locator($".mud-card:has-text('{algorithmName}')");
        var toggle = card.Locator(".mud-switch");
        await toggle.ClickAsync();
    }

    /// <summary>
    /// Clicks the "Save All Changes" button.
    /// </summary>
    public async Task ClickSaveAllChangesAsync()
    {
        var saveButton = Page.GetByRole(AriaRole.Button, new() { Name = "Save All Changes" }).First;
        await saveButton.ClickAsync();
    }

    /// <summary>
    /// Gets the Training Mode toggle state.
    /// The toggle is inside the Global Detection Settings paper, not the nav.
    /// </summary>
    public async Task<bool> IsTrainingModeEnabledAsync()
    {
        // Find the paper containing "Global Detection Settings" and then the Training Mode switch within it
        var globalSettingsPaper = Page.Locator(".mud-paper:has-text('Global Detection Settings')");
        var toggle = globalSettingsPaper.Locator(".mud-switch:has-text('Training Mode') input");
        return await toggle.IsCheckedAsync();
    }

    #endregion

    #region Stop Words Section

    /// <summary>
    /// Clicks the "Add Stop Word" button.
    /// </summary>
    public async Task ClickAddStopWordAsync()
    {
        var addButton = Page.GetByRole(AriaRole.Button, new() { Name = "Add Stop Word" });
        await addButton.ClickAsync();
    }

    /// <summary>
    /// Gets the count of stop words displayed in the table.
    /// </summary>
    public async Task<int> GetStopWordCountAsync()
    {
        // Count rows in the MudTable, excluding header and no-records
        var rows = Page.Locator(".mud-table tbody tr:not(.mud-table-row-no-records)");
        return await rows.CountAsync();
    }

    /// <summary>
    /// Searches for a stop word in the table. Waits for the table to reflect the filter.
    /// </summary>
    public async Task SearchStopWordsAsync(string searchText)
    {
        var searchField = Page.GetByPlaceholder("Search stop words...");
        await searchField.FillAsync(searchText);

        // Wait for the "Showing" text to update (indicates filter applied)
        // The filter updates client-side so we wait for the Showing text to be visible
        await Expect(Page.GetByText("Showing", new() { Exact = false })).ToBeVisibleAsync();
    }

    /// <summary>
    /// Checks if a specific stop word is visible in the table.
    /// </summary>
    public async Task<bool> IsStopWordVisibleAsync(string word)
    {
        var wordChip = Page.Locator($".mud-table-container .mud-chip:has-text('{word}')");
        return await wordChip.IsVisibleAsync();
    }

    /// <summary>
    /// Waits for a stop word to become visible in the table.
    /// </summary>
    public async Task WaitForStopWordVisibleAsync(string word)
    {
        var wordChip = Page.Locator($".mud-table-container .mud-chip:has-text('{word}')");
        await Expect(wordChip).ToBeVisibleAsync();
    }

    /// <summary>
    /// Waits for a stop word to become hidden from the table.
    /// </summary>
    public async Task WaitForStopWordHiddenAsync(string word)
    {
        var wordChip = Page.Locator($".mud-table-container .mud-chip:has-text('{word}')");
        await Expect(wordChip).Not.ToBeVisibleAsync();
    }

    /// <summary>
    /// Deletes a stop word by clicking its delete button.
    /// </summary>
    public async Task ClickDeleteStopWordAsync(string word)
    {
        // Find the row containing the word, then click its delete button
        var row = Page.Locator($".mud-table tbody tr:has-text('{word}')");
        var deleteButton = row.Locator("button[title='Delete']");
        await deleteButton.ClickAsync();
    }

    /// <summary>
    /// Confirms the delete action in the confirmation dialog.
    /// </summary>
    public async Task ConfirmDeleteAsync()
    {
        var dialog = Page.GetByRole(AriaRole.Dialog);
        await Expect(dialog).ToBeVisibleAsync();
        var deleteButton = dialog.GetByRole(AriaRole.Button, new() { Name = "Delete" });
        await deleteButton.ClickAsync();
    }

    #endregion

    #region Training Samples Section

    /// <summary>
    /// Clicks the "Add Training Sample" button on the Training Samples page.
    /// </summary>
    public async Task ClickAddTrainingSampleAsync()
    {
        var addButton = Page.GetByRole(AriaRole.Button, new() { Name = "Add Training Sample" });
        await addButton.ClickAsync();
    }

    /// <summary>
    /// Gets the count of training samples displayed.
    /// </summary>
    public async Task<int> GetTrainingSampleCountAsync()
    {
        // Count rows in the MudTable
        var rows = Page.Locator(".mud-table tbody tr:not(.mud-table-row-no-records)");
        return await rows.CountAsync();
    }

    /// <summary>
    /// Checks if a training sample containing the specified text is visible.
    /// </summary>
    public async Task<bool> IsTrainingSampleVisibleAsync(string sampleText)
    {
        var cell = Page.Locator($".mud-table-container:has-text('{sampleText}')");
        return await cell.IsVisibleAsync();
    }

    /// <summary>
    /// Selects a type filter on the Training Samples page.
    /// </summary>
    /// <param name="filterOption">The filter option text (e.g., "All", "Spam Only", "Ham Only")</param>
    public async Task SelectTypeFilterAsync(string filterOption)
    {
        // The Type Filter select is inside the MudStack with search field
        // Use the select with "Type Filter" label
        var typeFilterSelect = Page.Locator(".mud-select").Filter(new() { HasText = "Type Filter" }).First;
        await typeFilterSelect.ClickAsync();

        // Wait for the popover to appear
        var popover = Page.Locator(".mud-popover-open");
        await Expect(popover).ToBeVisibleAsync();

        // MudBlazor uses .mud-list-item for select options, not role="option"
        var option = popover.Locator(".mud-list-item").Filter(new() { HasText = filterOption }).First;
        await option.ClickAsync();

        // Wait for popover to close
        await Expect(popover).Not.ToBeVisibleAsync();
    }

    /// <summary>
    /// Selects a source filter on the Training Samples page.
    /// </summary>
    /// <param name="filterOption">The filter option text (e.g., "All Sources", "Manual", etc.)</param>
    public async Task SelectSourceFilterAsync(string filterOption)
    {
        // The Source Filter select is the second select in the row
        var sourceFilterSelect = Page.Locator(".mud-select").Filter(new() { HasText = "Source Filter" }).First;
        await sourceFilterSelect.ClickAsync();

        // Wait for the popover to appear
        var popover = Page.Locator(".mud-popover-open");
        await Expect(popover).ToBeVisibleAsync();

        // MudBlazor uses .mud-list-item for select options
        var option = popover.Locator(".mud-list-item").Filter(new() { HasText = filterOption }).First;
        await option.ClickAsync();

        // Wait for popover to close
        await Expect(popover).Not.ToBeVisibleAsync();
    }

    #endregion

    #region Background Jobs Section

    /// <summary>
    /// Navigates to the Background Jobs page.
    /// </summary>
    public async Task NavigateToBackgroundJobsAsync()
    {
        await NavigateToSectionAsync("system", "background-jobs");
    }

    /// <summary>
    /// Gets the count of background jobs displayed in the table.
    /// </summary>
    public async Task<int> GetBackgroundJobCountAsync()
    {
        var rows = Page.Locator(".mud-table tbody tr");
        return await rows.CountAsync();
    }

    /// <summary>
    /// Checks if a job is enabled by its display name.
    /// </summary>
    public async Task<bool> IsJobEnabledAsync(string jobDisplayName)
    {
        var row = Page.Locator($".mud-table tbody tr:has-text('{jobDisplayName}')");
        var toggle = row.Locator(".mud-switch input");
        return await toggle.IsCheckedAsync();
    }

    /// <summary>
    /// Gets the status text for a job (Enabled/Disabled).
    /// </summary>
    public async Task<string> GetJobStatusAsync(string jobDisplayName)
    {
        var row = Page.Locator($".mud-table tbody tr:has-text('{jobDisplayName}')");
        var statusChip = row.Locator(".mud-chip");
        return await statusChip.TextContentAsync() ?? string.Empty;
    }

    /// <summary>
    /// Gets the schedule text for a job.
    /// </summary>
    public async Task<string> GetJobScheduleAsync(string jobDisplayName)
    {
        var row = Page.Locator($".mud-table tbody tr:has-text('{jobDisplayName}')");
        // Schedule is in the 3rd column (after Job and Status)
        var scheduleCell = row.Locator("td").Nth(2);
        return await scheduleCell.TextContentAsync() ?? string.Empty;
    }

    /// <summary>
    /// Toggles a background job by clicking its switch.
    /// </summary>
    public async Task ToggleJobAsync(string jobDisplayName)
    {
        var row = Page.Locator($".mud-table tbody tr:has-text('{jobDisplayName}')");
        var toggle = row.Locator(".mud-switch");
        await toggle.ClickAsync();
    }

    /// <summary>
    /// Opens the configuration dialog for a job.
    /// </summary>
    public async Task OpenJobConfigDialogAsync(string jobDisplayName)
    {
        var row = Page.Locator($".mud-table tbody tr:has-text('{jobDisplayName}')");
        var settingsButton = row.Locator("button[title='Configure']");
        await settingsButton.ClickAsync();
    }

    /// <summary>
    /// Fills in the schedule in the configuration dialog and saves it.
    /// </summary>
    public async Task UpdateJobScheduleAsync(string newSchedule)
    {
        var dialog = Page.GetByRole(AriaRole.Dialog);
        await Expect(dialog).ToBeVisibleAsync();

        // Find the Schedule text field by label and fill it
        var scheduleField = dialog.GetByLabel("Schedule");
        await scheduleField.ClearAsync();
        await scheduleField.FillAsync(newSchedule);

        // Trigger validation by blurring
        await scheduleField.BlurAsync();

        // Click Save button
        var saveButton = dialog.GetByRole(AriaRole.Button, new() { Name = "Save" });
        await saveButton.ClickAsync();

        // Wait for dialog to close
        await Expect(dialog).Not.ToBeVisibleAsync();
    }

    /// <summary>
    /// Cancels the configuration dialog.
    /// </summary>
    public async Task CancelJobConfigDialogAsync()
    {
        var dialog = Page.GetByRole(AriaRole.Dialog);
        var cancelButton = dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" });
        await cancelButton.ClickAsync();

        // Wait for dialog to close
        await Expect(dialog).Not.ToBeVisibleAsync();
    }

    /// <summary>
    /// Waits for the snackbar to appear with a message containing the specified text.
    /// </summary>
    public async Task WaitForSnackbarAsync(string containsText)
    {
        await Expect(Page.Locator(".mud-snackbar").First).ToContainTextAsync(containsText, new() { IgnoreCase = true });
    }

    #endregion

    #region Service Messages Section

    /// <summary>
    /// Navigates to the Service Messages page.
    /// </summary>
    public async Task NavigateToServiceMessagesAsync()
    {
        await NavigateToSectionAsync("telegram", "service-messages");
    }

    /// <summary>
    /// Gets the count of service message toggle switches.
    /// </summary>
    public async Task<int> GetServiceMessageToggleCountAsync()
    {
        // Use label.mud-switch to only match actual switch elements, not paragraphs
        var toggles = Page.Locator("label.mud-switch");
        return await toggles.CountAsync();
    }

    /// <summary>
    /// Checks if a specific service message type is set to delete by its label text.
    /// </summary>
    public async Task<bool> IsServiceMessageDeletionEnabledAsync(string labelText)
    {
        // Use label.mud-switch to only match actual switch elements
        var switchContainer = Page.Locator($"label.mud-switch:has-text('{labelText}')");
        var input = switchContainer.Locator("input[type='checkbox']");
        return await input.IsCheckedAsync();
    }

    /// <summary>
    /// Toggles a service message deletion setting by its label text.
    /// </summary>
    public async Task ToggleServiceMessageDeletionAsync(string labelText)
    {
        // Use label.mud-switch to only match actual switch elements
        var switchContainer = Page.Locator($"label.mud-switch:has-text('{labelText}')");
        await switchContainer.ClickAsync();
    }

    /// <summary>
    /// Clicks the Save button on the Service Messages settings page.
    /// </summary>
    public async Task ClickSaveServiceMessagesAsync()
    {
        var saveButton = Page.GetByRole(AriaRole.Button, new() { Name = "Save" });
        await saveButton.ClickAsync();
    }

    /// <summary>
    /// Verifies that all expected service message toggles are visible.
    /// </summary>
    public async Task<bool> AreAllServiceMessageTogglesVisibleAsync()
    {
        var expectedLabels = new[]
        {
            "Delete Join Messages",
            "Delete Leave Messages",
            "Delete Photo Changes",
            "Delete Title Changes",
            "Delete Pin Notifications",
            "Delete Chat Creation Messages"
        };

        foreach (var label in expectedLabels)
        {
            // Use label.mud-switch to only match actual switch elements
            var toggle = Page.Locator($"label.mud-switch:has-text('{label}')");
            if (!await toggle.IsVisibleAsync())
                return false;
        }

        return true;
    }

    #endregion

    #region Dialog Interactions

    /// <summary>
    /// Fills in the Add Stop Word dialog and submits it.
    /// Uses proper Playwright waiting strategies instead of hardcoded delays.
    /// </summary>
    public async Task FillAndSubmitAddStopWordDialogAsync(string word, string? notes = null)
    {
        // Wait for dialog to appear
        var dialog = Page.GetByRole(AriaRole.Dialog);
        await Expect(dialog).ToBeVisibleAsync();

        // Fill in the word field - use the labeled input
        var wordField = dialog.GetByLabel("Stop Word");
        await wordField.FillAsync(word);

        // Trigger validation by pressing a key and waiting for the async validation to complete
        // The button becomes enabled when _word is not empty AND _wordExists is false
        await wordField.PressAsync("Tab");

        // Fill in notes if provided (this also helps trigger state updates)
        if (!string.IsNullOrEmpty(notes))
        {
            var notesField = dialog.GetByLabel("Notes (Optional)");
            await notesField.FillAsync(notes);
        }

        // Wait for the "Add Stop Word" button to become enabled
        // The button is disabled when: _isSubmitting || string.IsNullOrWhiteSpace(_word) || _wordExists
        var addButton = dialog.GetByRole(AriaRole.Button, new() { Name = "Add Stop Word" });
        await Expect(addButton).ToBeEnabledAsync(new() { Timeout = 5000 });
        await addButton.ClickAsync();

        // Wait for dialog to close (indicates submission completed)
        await Expect(dialog).Not.ToBeVisibleAsync();
    }

    /// <summary>
    /// Fills in the Add Sample dialog and submits it.
    /// </summary>
    public async Task FillAndSubmitAddSampleDialogAsync(string text, bool isSpam)
    {
        // Wait for dialog to appear
        var dialog = Page.GetByRole(AriaRole.Dialog);
        await Expect(dialog).ToBeVisibleAsync();

        // Fill in the text field
        var textField = dialog.Locator("textarea").First;
        await textField.FillAsync(text);

        // Select spam/ham classification
        if (isSpam)
        {
            var spamRadio = dialog.Locator(".mud-radio:has-text('Spam')");
            await spamRadio.ClickAsync();
        }
        else
        {
            var hamRadio = dialog.Locator(".mud-radio:has-text('Ham')");
            await hamRadio.ClickAsync();
        }

        // Click the Add button
        var addButton = dialog.GetByRole(AriaRole.Button, new() { Name = "Add" });
        await addButton.ClickAsync();

        // Wait for dialog to close
        await Expect(dialog).Not.ToBeVisibleAsync();
    }

    #endregion
}
