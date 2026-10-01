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
        // Settings page has complex interactivity - wait for the Blazor circuit to be live
        await Page.WaitForInteractiveAsync();
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
    /// The page title displayed in the settings content area (a MudText element with Typo h4).
    /// </summary>
    public ILocator PageTitle => Page.Locator("main .mud-text-h4, .mud-container h4, .mud-item h4").First;

    /// <summary>The settings nav menu.</summary>
    public ILocator NavMenu => Page.Locator(".mud-navmenu");

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
    /// The algorithm toggle switches on the Detection Algorithms page (MudSwitch elements in card headers).
    /// </summary>
    public ILocator AlgorithmToggles => Page.Locator(".mud-card-header .mud-switch");

    /// <summary>
    /// The checkbox input of the enable switch on the algorithm card named <paramref name="algorithmName"/>.
    /// Checked when the algorithm is enabled.
    /// </summary>
    public ILocator AlgorithmToggleInput(string algorithmName) =>
        // Use Filter() with HasText instead of string interpolation to avoid selector injection
        Page.Locator(".mud-card").Filter(new() { HasText = algorithmName }).Locator(".mud-switch input");

    /// <summary>
    /// Toggles an algorithm by its name.
    /// </summary>
    public async Task ToggleAlgorithmAsync(string algorithmName)
    {
        // Use Filter() with HasText instead of string interpolation to avoid selector injection
        var card = Page.Locator(".mud-card").Filter(new() { HasText = algorithmName });
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
    /// The checkbox input of the Training Mode switch; checked when Training Mode is enabled.
    /// The toggle is inside the Global Detection Settings paper, not the nav.
    /// </summary>
    public ILocator TrainingModeToggleInput =>
        Page.Locator(".mud-paper:has-text('Global Detection Settings')")
            .Locator(".mud-switch:has-text('Training Mode') input");

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
    /// The stop word rows in the MudTable, excluding header and no-records rows.
    /// </summary>
    public ILocator StopWordRows => Page.Locator(".mud-table tbody tr:not(.mud-table-row-no-records)");

    /// <summary>The stop words table container.</summary>
    public ILocator StopWordsTable => Page.Locator(".mud-table-container");

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
    /// The chip for stop word <paramref name="word"/> in the stop words table.
    /// </summary>
    public ILocator StopWordChip(string word) =>
        Page.Locator(".mud-table-container .mud-chip").Filter(new() { HasText = word });

    /// <summary>
    /// Waits for a stop word to become visible in the table.
    /// </summary>
    public async Task WaitForStopWordVisibleAsync(string word)
    {
        await Expect(StopWordChip(word)).ToBeVisibleAsync();
    }

    /// <summary>
    /// Waits for a stop word to become hidden from the table.
    /// </summary>
    public async Task WaitForStopWordHiddenAsync(string word)
    {
        await Expect(StopWordChip(word)).Not.ToBeVisibleAsync();
    }

    /// <summary>
    /// Deletes a stop word by clicking its delete button.
    /// </summary>
    public async Task ClickDeleteStopWordAsync(string word)
    {
        // Find the row containing the word, then click its delete button
        var row = Page.Locator(".mud-table tbody tr").Filter(new() { HasText = word });
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
    /// The training sample rows in the MudTable, excluding header and no-records rows.
    /// </summary>
    public ILocator TrainingSampleRows => Page.Locator(".mud-table tbody tr:not(.mud-table-row-no-records)");

    /// <summary>
    /// The training samples table container, filtered to one containing <paramref name="sampleText"/>.
    /// </summary>
    public ILocator TrainingSample(string sampleText) =>
        Page.Locator(".mud-table-container").Filter(new() { HasText = sampleText });

    /// <summary>
    /// Selects a type filter on the Training Samples page.
    /// </summary>
    /// <param name="filterOption">The filter option text (e.g., "All", "Spam Only", "Clean Only")</param>
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
    /// The background job rows in the jobs table.
    /// </summary>
    public ILocator BackgroundJobRows => Page.Locator(".mud-table tbody tr");

    /// <summary>
    /// The table row for the job with display name <paramref name="jobDisplayName"/>.
    /// </summary>
    public ILocator JobRow(string jobDisplayName) => BackgroundJobRows.Filter(new() { HasText = jobDisplayName });

    /// <summary>
    /// The checkbox input of the enable switch for a job; checked when the job is enabled.
    /// </summary>
    public ILocator JobToggleInput(string jobDisplayName) => JobRow(jobDisplayName).Locator(".mud-switch input");

    /// <summary>
    /// The status chip (Enabled/Disabled) for a job.
    /// </summary>
    public ILocator JobStatusChip(string jobDisplayName) => JobRow(jobDisplayName).Locator(".mud-chip");

    /// <summary>
    /// The schedule cell for a job: the 3rd column (after Job and Status).
    /// </summary>
    public ILocator JobScheduleCell(string jobDisplayName) => JobRow(jobDisplayName).Locator("td").Nth(2);

    /// <summary>
    /// Toggles a background job by clicking its switch.
    /// </summary>
    public async Task ToggleJobAsync(string jobDisplayName)
    {
        var row = Page.Locator(".mud-table tbody tr").Filter(new() { HasText = jobDisplayName });
        var toggle = row.Locator(".mud-switch");
        await toggle.ClickAsync();
    }

    /// <summary>
    /// Opens the configuration dialog for a job.
    /// </summary>
    public async Task OpenJobConfigDialogAsync(string jobDisplayName)
    {
        var row = Page.Locator(".mud-table tbody tr").Filter(new() { HasText = jobDisplayName });
        var settingsButton = row.Locator("button[title='Configure']");
        await settingsButton.ClickAsync();
        // Wait for dialog to appear
        await Expect(Page.GetByRole(AriaRole.Dialog)).ToBeVisibleAsync();
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
    /// The service message toggle switches. Uses label.mud-switch to only match actual switch
    /// elements, not paragraphs.
    /// </summary>
    public ILocator ServiceMessageToggles => Page.Locator("label.mud-switch");

    /// <summary>
    /// The switch for the service message type labelled <paramref name="labelText"/>.
    /// </summary>
    public ILocator ServiceMessageToggle(string labelText) => ServiceMessageToggles.Filter(new() { HasText = labelText });

    /// <summary>
    /// The checkbox input of the service message switch labelled <paramref name="labelText"/>;
    /// checked when that message type is set to delete.
    /// </summary>
    public ILocator ServiceMessageToggleInput(string labelText) =>
        ServiceMessageToggle(labelText).Locator("input[type='checkbox']");

    /// <summary>
    /// Toggles a service message deletion setting by its label text.
    /// </summary>
    public async Task ToggleServiceMessageDeletionAsync(string labelText)
    {
        // Use label.mud-switch to only match actual switch elements
        var switchContainer = Page.Locator("label.mud-switch").Filter(new() { HasText = labelText });
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
    /// Asserts that all expected service message toggles are visible.
    /// </summary>
    public async Task ExpectAllServiceMessageTogglesVisibleAsync()
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
            await Expect(ServiceMessageToggle(label)).ToBeVisibleAsync();
        }
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
