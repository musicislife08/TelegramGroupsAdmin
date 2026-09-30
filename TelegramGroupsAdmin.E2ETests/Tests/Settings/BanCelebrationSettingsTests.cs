using TelegramGroupsAdmin.E2ETests.PageObjects.Settings;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Settings;

/// <summary>
/// E2E tests for Ban Celebration Settings page.
/// Tests dialog interactions that cannot be tested with bUnit:
/// - Escape key dismissal
/// - Backdrop click behavior
/// - File upload flows
/// - Duplicate detection cleanup on dismiss
/// </summary>
[TestFixture]
public class BanCelebrationSettingsTests : SharedAuthenticatedTestBase
{
    private BanCelebrationSettingsPage _page = null!;

    [SetUp]
    public void SetUp()
    {
        _page = new BanCelebrationSettingsPage(Page);
    }

    #region Dialog Dismissal Tests

    [Test]
    public async Task AddGifDialog_ClosesOnEscapeKey()
    {
        // Arrange - login and navigate
        await LoginAsOwnerAsync();
        await _page.NavigateAsync();
        await _page.WaitForLoadAsync();

        // Act - open dialog
        await _page.OpenAddGifDialogAsync();

        // Assert - dialog is visible
        await Expect(_page.Dialog).ToBeVisibleAsync();

        // Act - press Escape
        await _page.CloseDialogByEscapeAsync();

        // Assert - dialog is closed
        await Expect(_page.Dialog).Not.ToBeVisibleAsync();
    }

    [Test]
    public async Task AddGifDialog_DoesNotCloseOnBackdropClick()
    {
        // Arrange - login and navigate
        await LoginAsOwnerAsync();
        await _page.NavigateAsync();
        await _page.WaitForLoadAsync();

        // Act - open dialog
        await _page.OpenAddGifDialogAsync();

        // Assert - dialog is visible
        await Expect(_page.Dialog).ToBeVisibleAsync();

        // Act - click backdrop (should NOT close with BackdropClick = false)
        await _page.ClickBackdropAsync();

        // Assert - dialog is STILL visible (backdrop click disabled for safety)
        await _page.ExpectDialogVisibleAsync();

        // Cleanup - close dialog properly
        await _page.CloseDialogByEscapeAsync();
    }

    [Test]
    public async Task AddGifDialog_ClosesOnCancelButton()
    {
        // Arrange - login and navigate
        await LoginAsOwnerAsync();
        await _page.NavigateAsync();
        await _page.WaitForLoadAsync();

        // Act - open dialog
        await _page.OpenAddGifDialogAsync();

        // Assert - dialog is visible
        await Expect(_page.Dialog).ToBeVisibleAsync();

        // Act - click Cancel button
        await _page.CloseDialogByCancelButtonAsync();

        // Assert - dialog is closed
        await Expect(_page.Dialog).Not.ToBeVisibleAsync();
    }

    #endregion

    #region Validation Tests

    [Test]
    public async Task AddGifDialog_SubmitDisabled_WhenNoFileSelected()
    {
        // Arrange - login and navigate
        await LoginAsOwnerAsync();
        await _page.NavigateAsync();
        await _page.WaitForLoadAsync();

        // Act - open dialog (starts on Upload File tab)
        await _page.OpenAddGifDialogAsync();

        // Assert - submit button is disabled when no file selected
        await Expect(_page.SubmitButton).ToBeDisabledAsync();

        // Cleanup
        await _page.CloseDialogByEscapeAsync();
    }

    [Test]
    public async Task AddGifDialog_SubmitDisabled_WhenNoUrlEntered()
    {
        // Arrange - login and navigate
        await LoginAsOwnerAsync();
        await _page.NavigateAsync();
        await _page.WaitForLoadAsync();

        // Act - open dialog and switch to URL tab
        await _page.OpenAddGifDialogAsync();
        await _page.SwitchToUrlTabAsync();

        // Assert - submit button is disabled when no URL entered
        await Expect(_page.SubmitButton).ToBeDisabledAsync();

        // Cleanup
        await _page.CloseDialogByEscapeAsync();
    }

    #endregion

    #region File Upload Tests

    [Test]
    public async Task AddGifDialog_FileUpload_ShowsSelectedFile()
    {
        // Arrange - login and navigate
        await LoginAsOwnerAsync();
        await _page.NavigateAsync();
        await _page.WaitForLoadAsync();

        // Create a test GIF file
        var testFilePath = await CreateTestGifFileAsync("test-upload.gif");

        try
        {
            // Act - open dialog and upload file
            await _page.OpenAddGifDialogAsync();
            await _page.UploadFileAsync(testFilePath);

            // Assert - file name is shown as selected (uses Expect with auto-waiting)
            await _page.WaitForFileSelectedAsync("test-upload.gif");

            // Assert - submit button becomes enabled
            await Expect(_page.SubmitButton).ToBeEnabledAsync();

            // Cleanup
            await _page.CloseDialogByEscapeAsync();
        }
        finally
        {
            // Cleanup test file
            if (File.Exists(testFilePath))
                File.Delete(testFilePath);
        }
    }

    [Test]
    public async Task AddGifDialog_FileUpload_SubmitAddsGif()
    {
        // Arrange - login and navigate
        await LoginAsOwnerAsync();
        await _page.NavigateAsync();
        await _page.WaitForLoadAsync();

#pragma warning disable RS0030 // Baseline count is needed to assert the library grows by exactly one; WaitForLoadAsync synced on the table
        var initialCount = await _page.GifRows.CountAsync();
#pragma warning restore RS0030

        // Create a test GIF file with unique name
        var uniqueName = $"test-{Guid.NewGuid():N}.gif";
        var testFilePath = await CreateTestGifFileAsync(uniqueName);

        try
        {
            // Act - open dialog, upload file, and submit
            await _page.OpenAddGifDialogAsync();
            await _page.UploadFileAsync(testFilePath);
            await _page.WaitForFileSelectedAsync(uniqueName);
            await _page.SubmitAndWaitForCloseAsync();

            // Assert - snackbar shows success
            await Expect(_page.Snackbar).ToContainTextAsync("added", new() { IgnoreCase = true });

            // Assert - GIF count increased by 1
            await Expect(_page.GifRows).ToHaveCountAsync(initialCount + 1);
        }
        finally
        {
            // Cleanup test file
            if (File.Exists(testFilePath))
                File.Delete(testFilePath);
        }
    }

    #endregion

    #region Caption Dialog Tests

    [Test]
    public async Task AddCaptionDialog_ClosesOnEscapeKey()
    {
        // Arrange - login and navigate
        await LoginAsOwnerAsync();
        await _page.NavigateAsync();
        await _page.WaitForLoadAsync();

        // Act - open caption dialog
        await _page.OpenAddCaptionDialogAsync();

        // Assert - dialog is visible
        await Expect(_page.Dialog).ToBeVisibleAsync();

        // Act - press Escape
        await Page.Keyboard.PressAsync("Escape");

        // Assert - dialog closes
        await Expect(_page.Dialog).Not.ToBeVisibleAsync();
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a minimal valid GIF file for testing.
    /// </summary>
    private static async Task<string> CreateTestGifFileAsync(string name = "test.gif")
    {
        var path = Path.Combine(Path.GetTempPath(), name);

        // Minimal valid GIF89a (1x1 pixel, transparent)
        var gifBytes = new byte[]
        {
            0x47, 0x49, 0x46, 0x38, 0x39, 0x61, // GIF89a header
            0x01, 0x00, 0x01, 0x00, 0x80, 0x00, // Logical screen descriptor (1x1)
            0x00, 0xFF, 0xFF, 0xFF, 0x00, 0x00, // Global color table
            0x00, 0x21, 0xF9, 0x04, 0x00, 0x00, // Graphics control extension
            0x00, 0x00, 0x2C, 0x00, 0x00, 0x00, // Image descriptor
            0x00, 0x01, 0x00, 0x01, 0x00, 0x00, // Width, height
            0x02, 0x02, 0x44, 0x01, 0x00, 0x3B  // Image data and trailer
        };

        await File.WriteAllBytesAsync(path, gifBytes);
        return path;
    }

    #endregion
}
