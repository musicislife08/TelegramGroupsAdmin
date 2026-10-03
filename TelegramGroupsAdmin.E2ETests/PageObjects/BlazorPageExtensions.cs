using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.PageObjects;

public static class BlazorPageExtensions
{
    /// <summary>
    /// Waits until the interactive Blazor circuit has rendered the page. Pages are prerendered first, then
    /// the circuit re-runs OnInitializedAsync and re-renders them; MainLayout's data-interactive marker only
    /// reads "true" in an interactive render, so after this returns the prerendered DOM is gone and later
    /// waits see the live component. (NetworkIdle cannot tell: the circuit's renders travel over the WebSocket.)
    /// </summary>
    public static Task WaitForInteractiveAsync(this IPage page, int timeoutMs = 15000)
        => Expect(page.Locator("[data-interactive='true']")).ToBeAttachedAsync(new() { Timeout = timeoutMs });
}
