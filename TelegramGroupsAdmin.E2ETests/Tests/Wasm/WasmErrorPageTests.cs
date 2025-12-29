using TelegramGroupsAdmin.E2ETests.PageObjects;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Wasm;

/// <summary>
/// WASM Tests for the Error page (/error).
/// Verifies error page displays correctly and navigation works.
/// Uses WasmSharedAuthenticatedTestBase for faster test execution with shared factory.
/// </summary>
[TestFixture]
public class WasmErrorPageTests : WasmSharedAuthenticatedTestBase
{
    private ErrorPage _errorPage = null!;

    [SetUp]
    public void SetUp()
    {
        _errorPage = new ErrorPage(Page);
    }

    [Test]
    public async Task ErrorPage_DisplaysErrorMessage()
    {
        // Arrange
        await LoginAsOwnerAsync();

        // Act
        await _errorPage.NavigateAsync();

        // Assert - uses Playwright Expect with auto-retry (WaitForLoadAsync uses Expect internally)
        await _errorPage.WaitForLoadAsync();
    }

    [Test]
    public async Task ErrorPage_ReturnHomeButton_NavigatesToHome()
    {
        // Arrange
        await LoginAsOwnerAsync();
        await _errorPage.NavigateAsync();
        await _errorPage.WaitForLoadAsync();

        // Act
        await _errorPage.ClickReturnHomeAsync();

        // Assert - should be on home page (not error page) - Expect has auto-retry
        await Expect(Page).Not.ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/error"));
    }
}
