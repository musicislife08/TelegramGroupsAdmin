using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.PageObjects;

/// <summary>
/// Page object for DocsPage.razor (/docs - the documentation page).
/// Displays navigation tree and markdown content.
/// </summary>
public class DocsPage
{
    private readonly IPage _page;

    // MudBlazor CSS selectors (stable class names from the framework)
    // Note: MudNavMenu renders as "mud-navmenu" (no hyphen), but MudNavLink is "mud-nav-link" (with hyphen)
    private const string MarkdownContent = ".markdown-content";

    // Locator getters for reuse
    // Use the MudPaper container that has the "Docs" header to scope our nav selectors
    private ILocator DocsNavContainerLocator => _page.Locator(".mud-paper").Filter(new() { Has = _page.GetByText("Docs", new() { Exact = true }) });
    private ILocator NavMenuLocator => DocsNavContainerLocator.Locator(".mud-navmenu").First;
    private ILocator NavLinksLocator => DocsNavContainerLocator.Locator(".mud-nav-link");
    private ILocator MarkdownContentLocator => _page.Locator(MarkdownContent);
    private ILocator NotFoundLocator => _page.GetByText("not found", new() { Exact = false });

    public DocsPage(IPage page)
    {
        _page = page;
    }

    /// <summary>
    /// Navigates to the docs page, optionally with a specific path.
    /// </summary>
    public async Task NavigateAsync(string? path = null)
    {
        var url = path == null ? "/docs" : $"/docs/{path}";
        await _page.GotoAsync(url);
        await _page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
    }

    /// <summary>
    /// Waits for the docs page to fully load (nav visible).
    /// Uses Playwright's Expect() for built-in auto-retry.
    /// </summary>
    public async Task WaitForLoadAsync(int timeoutMs = 15000)
    {
        // Wait for nav menu to be visible (auto-retry with Expect)
        await Expect(NavMenuLocator).ToBeVisibleAsync(new() { Timeout = timeoutMs });
    }

    /// <summary>
    /// Asserts that navigation has at least one item (with auto-retry).
    /// Uses First.ToBeVisibleAsync which waits for at least one element to exist.
    /// </summary>
    public async Task AssertNavHasItemsAsync(int minCount = 1, int timeoutMs = 10000)
    {
        // ToHaveCountAsync expects exact count; for "at least 1" we verify first item is visible
        await Expect(NavLinksLocator.First).ToBeVisibleAsync(new() { Timeout = timeoutMs });
    }

    /// <summary>
    /// Asserts that document content is visible (with auto-retry).
    /// </summary>
    public async Task AssertContentVisibleAsync(int timeoutMs = 10000)
    {
        await Expect(MarkdownContentLocator).ToBeVisibleAsync(new() { Timeout = timeoutMs });
    }

    /// <summary>
    /// Asserts that "not found" message is visible (with auto-retry).
    /// </summary>
    public async Task AssertNotFoundVisibleAsync(int timeoutMs = 10000)
    {
        await Expect(NotFoundLocator).ToBeVisibleAsync(new() { Timeout = timeoutMs });
    }

    /// <summary>
    /// Gets the count of navigation items.
    /// </summary>
    public async Task<int> GetNavItemCountAsync()
    {
        return await NavLinksLocator.CountAsync();
    }

    /// <summary>
    /// Checks if the markdown content area is visible.
    /// </summary>
    public async Task<bool> IsDocumentContentVisibleAsync()
    {
        return await MarkdownContentLocator.IsVisibleAsync();
    }

    /// <summary>
    /// Clicks the first navigation item.
    /// </summary>
    public async Task ClickFirstNavItemAsync()
    {
        await NavLinksLocator.First.ClickAsync();
    }

    /// <summary>
    /// Checks if the "not found" message is visible.
    /// </summary>
    public async Task<bool> IsNotFoundVisibleAsync()
    {
        return await NotFoundLocator.IsVisibleAsync();
    }
}
