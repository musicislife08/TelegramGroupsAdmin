using TelegramGroupsAdmin.E2ETests.PageObjects;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Wasm;

/// <summary>
/// WASM Tests for the DocsPage (/docs).
/// Structural smoke tests - verify the feature works, not specific content.
/// Uses WasmSharedAuthenticatedTestBase for faster test execution with shared factory.
/// </summary>
[TestFixture]
public class WasmDocsTests : WasmSharedAuthenticatedTestBase
{
    private DocsPage _docsPage = null!;

    [SetUp]
    public void SetUp()
    {
        _docsPage = new DocsPage(Page);
    }

    [Test]
    public async Task DocsPage_NavLoads_HasAtLeastOneItem()
    {
        // Arrange
        await LoginAsOwnerAsync();

        // Act
        await _docsPage.NavigateAsync();
        await _docsPage.WaitForLoadAsync();

        // Assert - nav should have at least one item (uses Playwright Expect with auto-retry)
        await _docsPage.AssertNavHasItemsAsync();
    }

    [Test]
    public async Task DocsPage_ClickingNavItem_LoadsContent()
    {
        // Arrange
        await LoginAsOwnerAsync();
        await _docsPage.NavigateAsync();
        await _docsPage.WaitForLoadAsync();

        // Act - click first nav item
        await _docsPage.ClickFirstNavItemAsync();

        // Assert - content area should be visible (uses Playwright Expect with auto-retry)
        await _docsPage.AssertContentVisibleAsync();
    }

    [Test]
    public async Task DocsPage_InvalidPath_ShowsNotFound()
    {
        // Arrange
        await LoginAsOwnerAsync();

        // Act - navigate to a definitely-nonexistent path
        await _docsPage.NavigateAsync("definitely-not-a-real-path-12345");

        // Assert - should show not found message (uses Playwright Expect with auto-retry)
        await _docsPage.AssertNotFoundVisibleAsync();
    }
}
