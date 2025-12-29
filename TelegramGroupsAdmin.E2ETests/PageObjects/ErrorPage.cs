using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.PageObjects;

/// <summary>
/// Page object for Error.razor (/error - the error page).
/// Displays a generic error message and provides a way to return home.
/// </summary>
public class ErrorPage
{
    private readonly IPage _page;

    // Reusable locator properties
    private ILocator ErrorMessageLocator => _page.GetByText("Something went wrong");
    private ILocator ReturnHomeButtonLocator => _page.GetByRole(AriaRole.Link, new() { Name = "Return to Home" });

    public ErrorPage(IPage page)
    {
        _page = page;
    }

    /// <summary>
    /// Navigates to the error page.
    /// </summary>
    public async Task NavigateAsync()
    {
        await _page.GotoAsync("/error");
        await _page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
    }

    /// <summary>
    /// Waits for the error page content to be visible.
    /// Uses Playwright Expect() for auto-retry.
    /// </summary>
    public async Task WaitForLoadAsync(int timeoutMs = 10000)
    {
        await Expect(ErrorMessageLocator).ToBeVisibleAsync(new() { Timeout = timeoutMs });
    }

    /// <summary>
    /// Checks if the error message is visible.
    /// Note: Prefer WaitForLoadAsync() for assertions - this method does not auto-retry.
    /// </summary>
    public async Task<bool> IsErrorMessageVisibleAsync()
    {
        return await ErrorMessageLocator.IsVisibleAsync();
    }

    /// <summary>
    /// Checks if the "Return to Home" button is visible.
    /// </summary>
    public async Task<bool> HasReturnHomeButtonAsync()
    {
        return await ReturnHomeButtonLocator.IsVisibleAsync();
    }

    /// <summary>
    /// Clicks the "Return to Home" button.
    /// </summary>
    public async Task ClickReturnHomeAsync()
    {
        await ReturnHomeButtonLocator.ClickAsync();
    }
}
