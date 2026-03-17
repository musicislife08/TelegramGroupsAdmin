using Microsoft.Playwright;

namespace TelegramGroupsAdmin.E2ETests.Tests.Analytics;

/// <summary>
/// Tests that the timezone cascade prerender safety works correctly.
/// Verifies that cold page loads (prerender → Blazor circuit connect lifecycle)
/// do not produce JSException or JavaScript interop errors in the browser console.
///
/// Background: MainLayout.razor detects the user's timezone in OnAfterRenderAsync(firstRender),
/// which only fires after the SignalR circuit connects (post-prerender). This ensures
/// JsRuntime.InvokeAsync is never called during the SSR prerender pass where JS interop
/// is unavailable. LocalTimestamp components display UTC during prerender (UserTimeZone is null)
/// and convert to local time after the cascade resolves.
///
/// Covers FRONT-02: No JSException or "JavaScript interop calls cannot be issued" errors
/// during cold page loads.
/// </summary>
[TestFixture]
public class TimezonePreRenderTests : SharedAuthenticatedTestBase
{
    [Test]
    public async Task ColdPageLoad_NoJSExceptionInConsole()
    {
        // Arrange — collect console errors before navigating
        var consoleErrors = new List<string>();
        Page.Console += (_, msg) =>
        {
            if (msg.Type == "error")
                consoleErrors.Add(msg.Text);
        };

        // Login via cookie injection (no UI login flow)
        await LoginAsOwnerAsync();

        // Act — cold page load of the home page (uses LocalTimestamp components in dashboard)
        await NavigateToAsync("/");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait briefly for any delayed errors that fire asynchronously when the circuit connects
        await Task.Delay(1000);

        // Assert — no JSException or JavaScript interop errors in console
        var jsInteropErrors = consoleErrors
            .Where(e => e.Contains("JSException", StringComparison.OrdinalIgnoreCase)
                     || e.Contains("JavaScript interop calls cannot be issued", StringComparison.OrdinalIgnoreCase)
                     || e.Contains("InvalidOperationException", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.That(jsInteropErrors, Is.Empty,
            $"Found {jsInteropErrors.Count} JS interop error(s) during cold page load: {string.Join("; ", jsInteropErrors)}");
    }

    [Test]
    public async Task AnalyticsPage_ColdLoad_NoJSExceptionInConsole()
    {
        // Arrange — collect console errors before navigating
        var consoleErrors = new List<string>();
        Page.Console += (_, msg) =>
        {
            if (msg.Type == "error")
                consoleErrors.Add(msg.Text);
        };

        // Login via cookie injection (no UI login flow)
        await LoginAsOwnerAsync();

        // Act — cold page load of the analytics page
        await NavigateToAsync("/analytics");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Click Message Trends tab — exercises analytics components with UserTimeZone cascading parameter
        // (these components previously had per-component timezone detection that caused JSExceptions)
        await Page.GetByRole(AriaRole.Tab, new() { Name = "Message Trends" }).ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait briefly for any delayed errors that fire asynchronously when the circuit connects
        await Task.Delay(1000);

        // Assert — no JSException or JavaScript interop errors in console
        var jsInteropErrors = consoleErrors
            .Where(e => e.Contains("JSException", StringComparison.OrdinalIgnoreCase)
                     || e.Contains("JavaScript interop calls cannot be issued", StringComparison.OrdinalIgnoreCase)
                     || e.Contains("InvalidOperationException", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.That(jsInteropErrors, Is.Empty,
            $"Found {jsInteropErrors.Count} JS interop error(s) during analytics cold load with Message Trends tab: {string.Join("; ", jsInteropErrors)}");
    }
}
