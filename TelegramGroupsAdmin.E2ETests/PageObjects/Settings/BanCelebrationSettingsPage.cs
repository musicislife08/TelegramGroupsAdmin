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
    private const string GifTableRow = ".mud-paper:has-text('GIF Library') .mud-table-body tr:has(td[data-label])";

    // Selectors - Caption Section
    private const string AddCaptionButton = "button:has-text('Add Caption')";
    private const string CaptionTable = ".mud-paper:has-text('Caption Library') .mud-table";
    // Exclude NoRecordsContent row by requiring td with DataLabel attribute (actual data rows)
    private const string CaptionTableRow = ".mud-paper:has-text('Caption Library') .mud-table-body tr:has(td[data-label])";

    // Selectors - Dialog (shared)
    private const string Dialog = "[role='dialog']";
    private const string DialogTitle = ".mud-dialog-title";
    private const string DialogContent = ".mud-dialog-content";
    private const string DialogActions = ".mud-dialog-actions";
    private const string Backdrop = ".mud-overlay";
    private const string LoadingIndicator = ".mud-progress-linear";

    // Selectors - Add GIF Dialog
    private const string FileInput = "input[type='file']";
    private const string UrlTab = ".mud-tab:has-text('From URL')";
    private const string UploadTab = ".mud-tab:has-text('Upload File')";
    private const string UrlInput = "input[placeholder*='example.com']";
    private const string NameInput = ".mud-dialog input[aria-label='Name (optional)'], .mud-dialog .mud-input-slot:has-text('Name') input";
    private const string SubmitGifButton = ".mud-dialog-actions button:has-text('Add GIF')";
    private const string CancelButton = ".mud-dialog-actions button:has-text('Cancel')";

    // Selectors - Duplicate Warning
    private const string DuplicateWarning = ".mud-alert:has-text('Similar GIF')";
    private const string KeepBothButton = "button:has-text('Keep Both')";
    private const string CancelUploadButton = "button:has-text('Cancel Upload')";

    // Selectors - Snackbar
    private const string Snackbar = ".mud-snackbar";

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
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
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
        await _page.Locator(Dialog).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
    }

    /// <summary>
    /// Checks if a dialog is currently visible.
    /// </summary>
    public async Task<bool> IsDialogVisibleAsync()
    {
        return await _page.Locator(Dialog).IsVisibleAsync();
    }

    /// <summary>
    /// Gets the dialog title text.
    /// </summary>
    public async Task<string?> GetDialogTitleAsync()
    {
        return await _page.Locator(DialogTitle).TextContentAsync();
    }

    /// <summary>
    /// Closes the dialog by pressing Escape key.
    /// </summary>
    public async Task CloseDialogByEscapeAsync()
    {
        await _page.Keyboard.PressAsync("Escape");

        // Wait for dialog to close
        await _page.Locator(Dialog).WaitForAsync(new LocatorWaitForOptions
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
        await _page.Locator(Dialog).WaitForAsync(new LocatorWaitForOptions
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
    /// Switches to the Upload File tab in the dialog.
    /// </summary>
    public async Task SwitchToUploadTabAsync()
    {
        await _page.Locator(UploadTab).ClickAsync();
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
    /// Checks if the submit button is enabled.
    /// </summary>
    public async Task<bool> IsSubmitEnabledAsync()
    {
        var button = _page.Locator(SubmitGifButton);
        var isDisabled = await button.IsDisabledAsync();
        return !isDisabled;
    }

    /// <summary>
    /// Submits and waits for dialog to close (for successful uploads).
    /// </summary>
    public async Task SubmitAndWaitForCloseAsync()
    {
        await SubmitAsync();

        // Wait for dialog to close
        await _page.Locator(Dialog).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 10000  // Allow time for upload processing
        });
    }

    #endregion

    #region Duplicate Warning

    /// <summary>
    /// Checks if the duplicate warning is visible.
    /// </summary>
    public async Task<bool> IsDuplicateWarningVisibleAsync()
    {
        return await _page.Locator(DuplicateWarning).IsVisibleAsync();
    }

    /// <summary>
    /// Waits for the duplicate warning to appear.
    /// </summary>
    public async Task WaitForDuplicateWarningAsync(int timeoutMs = 10000)
    {
        await _page.Locator(DuplicateWarning).WaitForAsync(new LocatorWaitForOptions
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
        await _page.Locator(Dialog).WaitForAsync(new LocatorWaitForOptions
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
        await _page.Locator(Dialog).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 5000
        });
    }

    #endregion

    #region GIF List

    /// <summary>
    /// Gets the count of GIFs in the library.
    /// </summary>
    public async Task<int> GetGifCountAsync()
    {
        return await _page.Locator(GifTableRow).CountAsync();
    }

    /// <summary>
    /// Checks if a GIF with the given name exists in the table.
    /// </summary>
    public async Task<bool> GifExistsAsync(string name)
    {
        var row = _page.Locator(GifTableRow).Filter(new() { HasText = name });
        return await row.CountAsync() > 0;
    }

    #endregion

    #region Caption List

    /// <summary>
    /// Opens the Add Caption dialog.
    /// </summary>
    public async Task OpenAddCaptionDialogAsync()
    {
        await _page.Locator(AddCaptionButton).ClickAsync();

        // Wait for dialog to appear
        await _page.Locator(Dialog).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
    }

    /// <summary>
    /// Gets the count of captions in the library.
    /// </summary>
    public async Task<int> GetCaptionCountAsync()
    {
        return await _page.Locator(CaptionTableRow).CountAsync();
    }

    #endregion

    #region Snackbar

    /// <summary>
    /// Waits for a snackbar message to appear and returns its text.
    /// </summary>
    public async Task<string?> WaitForSnackbarAsync(int timeoutMs = 5000)
    {
        var snackbar = _page.Locator(Snackbar);
        await snackbar.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = timeoutMs
        });
        return await snackbar.TextContentAsync();
    }

    /// <summary>
    /// Checks if a snackbar with specific text is visible.
    /// </summary>
    public async Task<bool> IsSnackbarVisibleWithTextAsync(string text)
    {
        var snackbar = _page.Locator(Snackbar).Filter(new() { HasText = text });
        return await snackbar.IsVisibleAsync();
    }

    #endregion
}
