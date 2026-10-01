using Microsoft.Playwright;
using TelegramGroupsAdmin.Constants;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.PageObjects.Settings;

/// <summary>
/// Page object for the Ban Celebration settings section.
/// Provides methods to manage GIFs, captions, and test dialog interactions.
/// </summary>
public class BanCelebrationSettingsPage
{
    private readonly IPage _page;

    // Route path from constants
    private static readonly string PagePath = SettingsRoutes.BuildPath(
        SettingsRoutes.Moderation.Section,
        SettingsRoutes.Moderation.BanCelebration);

    // Selectors - GIF Section
    private const string AddGifButton = "button:has-text('Add GIF')";
    private const string GifTable = ".mud-paper:has-text('GIF Library') .mud-table";
    // Exclude NoRecordsContent row by requiring td with DataLabel attribute (actual data rows)
    private const string GifTableRowSelector = ".mud-paper:has-text('GIF Library') .mud-table-body tr:has(td[data-label])";

    // Selectors - Caption Section
    private const string AddCaptionButton = "button:has-text('Add Caption')";
    // Exclude NoRecordsContent row by requiring td with DataLabel attribute (actual data rows)
    private const string CaptionTableRowSelector = ".mud-paper:has-text('Caption Library') .mud-table-body tr:has(td[data-label])";

    // Selectors - Dialog (shared)
    private const string DialogSelector = "[role='dialog']";
    private const string DialogTitleSelector = ".mud-dialog-title";
    private const string DialogContent = ".mud-dialog-content";
    private const string Backdrop = ".mud-overlay";
    private const string LoadingIndicator = ".mud-progress-linear";

    // Selectors - Add GIF Dialog
    private const string FileInput = "input[type='file']";
    private const string UrlTab = ".mud-tab:has-text('From URL')";
    private const string UrlInput = "input[placeholder*='example.com']";
    private const string NameInput = ".mud-dialog input[aria-label='Name (optional)'], .mud-dialog .mud-input-slot:has-text('Name') input";
    private const string SubmitGifButton = ".mud-dialog-actions button:has-text('Add GIF')";
    private const string CancelButton = ".mud-dialog-actions button:has-text('Cancel')";

    // Selectors - Duplicate Warning
    private const string DuplicateWarningSelector = ".mud-alert:has-text('Similar GIF')";
    private const string KeepBothButton = "button:has-text('Keep Both')";
    private const string CancelUploadButton = "button:has-text('Cancel Upload')";

    // Selectors - Snackbar
    private const string SnackbarSelector = ".mud-snackbar";

    public BanCelebrationSettingsPage(IPage page)
    {
        _page = page;
    }

    #region Navigation

    /// <summary>
    /// Navigates to the Ban Celebration settings page.
    /// </summary>
    public async Task NavigateAsync()
    {
        await _page.GotoAsync(PagePath);
        await _page.WaitForInteractiveAsync();
    }

    /// <summary>
    /// Waits for the page to finish loading.
    /// </summary>
    public async Task WaitForLoadAsync(int timeoutMs = 15000)
    {
        // Wait for GIF table to be visible
        await _page.Locator(GifTable).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = timeoutMs
        });

        // Wait for loading indicator to disappear
        var loadingIndicator = _page.Locator(LoadingIndicator);
        await Expect(loadingIndicator).Not.ToBeVisibleAsync(new() { Timeout = 5000 });
    }

    #endregion

    #region Dialog Interactions

    /// <summary>
    /// Opens the Add GIF dialog by clicking the Add GIF button.
    /// </summary>
    public async Task OpenAddGifDialogAsync()
    {
        await _page.Locator(AddGifButton).ClickAsync();

        // Wait for dialog to appear
        await _page.Locator(DialogSelector).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
    }

    /// <summary>The dialog (shared by the Add GIF and Add Caption dialogs).</summary>
    public ILocator Dialog => _page.Locator(DialogSelector);

    /// <summary>
    /// Asserts that the dialog is visible using Playwright's auto-retrying Expect API.
    /// </summary>
    public async Task ExpectDialogVisibleAsync()
    {
        await Expect(Dialog).ToBeVisibleAsync();
    }

    /// <summary>The dialog title.</summary>
    public ILocator DialogTitle => _page.Locator(DialogTitleSelector);

    /// <summary>
    /// Closes the dialog by pressing Escape key.
    /// Clicks the dialog first to ensure it has focus (Escape only works when the dialog is focused).
    /// </summary>
    public async Task CloseDialogByEscapeAsync()
    {
        // MudBlazor handles Escape on the dialog element — ensure it has focus
        await _page.Locator(DialogSelector).ClickAsync();
        await _page.Keyboard.PressAsync("Escape");

        // Wait for dialog to close
        await _page.Locator(DialogSelector).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 5000
        });
    }

    /// <summary>
    /// Clicks the backdrop overlay (outside the dialog).
    /// Note: With BackdropClick = false, this should NOT close the dialog.
    /// </summary>
    public async Task ClickBackdropAsync()
    {
        var backdrop = _page.Locator(Backdrop);
        await backdrop.ClickAsync(new LocatorClickOptions
        {
            Position = new Position { X = 10, Y = 10 }  // Click near edge to avoid dialog
        });
    }

    /// <summary>
    /// Closes the dialog by clicking the Cancel button.
    /// </summary>
    public async Task CloseDialogByCancelButtonAsync()
    {
        await _page.Locator(CancelButton).ClickAsync();

        // Wait for dialog to close
        await _page.Locator(DialogSelector).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 5000
        });
    }

    #endregion

    #region File Upload

    /// <summary>
    /// Uploads a file using the file input.
    /// </summary>
    public async Task UploadFileAsync(string filePath)
    {
        // MudFileUpload uses a hidden file input
        var fileInput = _page.Locator(FileInput);
        await fileInput.SetInputFilesAsync(filePath);
    }

    /// <summary>
    /// Waits for the file to be shown as selected (by name).
    /// Uses Playwright's auto-waiting Expect assertions.
    /// </summary>
    public async Task WaitForFileSelectedAsync(string fileName)
    {
        var dialogContent = _page.Locator(DialogContent);
        // Look for the "Selected: filename" text that appears after file selection
        var selectedText = dialogContent.Locator($"text=Selected: {fileName}");
        await Expect(selectedText).ToBeVisibleAsync();
    }

    #endregion

    #region URL Upload

    /// <summary>
    /// Switches to the URL tab in the dialog.
    /// </summary>
    public async Task SwitchToUrlTabAsync()
    {
        await _page.Locator(UrlTab).ClickAsync();

        // Wait for URL input to be visible
        await _page.Locator(UrlInput).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 3000
        });
    }

    /// <summary>
    /// Enters a URL in the URL input field.
    /// </summary>
    public async Task EnterUrlAsync(string url)
    {
        await _page.Locator(UrlInput).FillAsync(url);
    }

    #endregion

    #region Submit

    /// <summary>
    /// Clicks the Add GIF submit button.
    /// </summary>
    public async Task SubmitAsync()
    {
        await _page.Locator(SubmitGifButton).ClickAsync();
    }

    /// <summary>
    /// The Add GIF submit button in the dialog actions. Disabled until a file or URL is provided.
    /// </summary>
    public ILocator SubmitButton => _page.Locator(SubmitGifButton);

    /// <summary>
    /// Submits and waits for dialog to close (for successful uploads).
    /// </summary>
    public async Task SubmitAndWaitForCloseAsync()
    {
        await SubmitAsync();

        // Wait for dialog to close
        await _page.Locator(DialogSelector).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 10000  // Allow time for upload processing
        });
    }

    #endregion

    #region Duplicate Warning

    /// <summary>The "Similar GIF" duplicate warning alert.</summary>
    public ILocator DuplicateWarning => _page.Locator(DuplicateWarningSelector);

    /// <summary>
    /// Waits for the duplicate warning to appear.
    /// </summary>
    public async Task WaitForDuplicateWarningAsync(int timeoutMs = 10000)
    {
        await DuplicateWarning.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = timeoutMs
        });
    }

    /// <summary>
    /// Clicks the "Keep Both" button in the duplicate warning.
    /// </summary>
    public async Task ClickKeepBothAsync()
    {
        await _page.Locator(KeepBothButton).ClickAsync();

        // Wait for dialog to close
        await _page.Locator(DialogSelector).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 5000
        });
    }

    /// <summary>
    /// Clicks the "Cancel Upload" button in the duplicate warning.
    /// </summary>
    public async Task ClickCancelUploadAsync()
    {
        await _page.Locator(CancelUploadButton).ClickAsync();

        // Wait for dialog to close
        await _page.Locator(DialogSelector).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 5000
        });
    }

    #endregion

    #region GIF List

    /// <summary>The GIF library data rows (excludes the no-records row).</summary>
    public ILocator GifRows => _page.Locator(GifTableRowSelector);

    /// <summary>The GIF library row containing <paramref name="name"/>.</summary>
    public ILocator GifRow(string name) => GifRows.Filter(new() { HasText = name });

    #endregion

    #region Caption List

    /// <summary>
    /// Opens the Add Caption dialog.
    /// </summary>
    public async Task OpenAddCaptionDialogAsync()
    {
        await _page.Locator(AddCaptionButton).ClickAsync();

        // Wait for dialog to appear
        await _page.Locator(DialogSelector).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
    }

    /// <summary>The caption library data rows (excludes the no-records row).</summary>
    public ILocator CaptionRows => _page.Locator(CaptionTableRowSelector);

    #endregion

    #region Snackbar

    /// <summary>The snackbar.</summary>
    public ILocator Snackbar => _page.Locator(SnackbarSelector);

    #endregion
}
