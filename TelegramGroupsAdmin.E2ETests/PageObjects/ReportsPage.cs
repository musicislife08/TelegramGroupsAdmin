using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.PageObjects;

/// <summary>
/// Page Object for the Reports Queue page (/reports).
/// Provides methods to interact with report filters, cards, and actions.
/// </summary>
public class ReportsPage
{
    private readonly IPage _page;

    // Selectors
    private const string PageTitleSelector = ".mud-typography-h4";
    private const string LoadingIndicatorSelector = ".mud-progress-linear";
    private const string FilterPaper = ".mud-paper.pa-3.mb-4";
    private const string TypeFilterSelect = "label:has-text('Type')";
    private const string StatusFilterSelect = "label:has-text('Status')";
    private const string RefreshButton = "button:has-text('Refresh')";
    private const string PendingModerationChipSelector = ".mud-chip:has-text('Moderation')";
    private const string PendingImpersonationChipSelector = ".mud-chip:has-text('Impersonation')";
    private const string ReportCards = ".mud-stack .mud-card";
    private const string EmptyStateIcon = ".mud-icon-root.mud-success-text";

    // Report card selectors - All use MudCard component
    private const string ModerationReportCard = ".mud-card:has-text('Moderation Report')";
    private const string ImpersonationAlertCard = ".mud-card:has-text('Impersonation Alert')";
    private const string ExamReviewCard = ".mud-card:has-text('Exam Review')";
    private const string PendingExamChipSelector = ".mud-chip:has-text('Exam')";

    public ReportsPage(IPage page)
    {
        _page = page;
    }

    /// <summary>
    /// Navigates to the Reports page.
    /// </summary>
    public async Task NavigateAsync()
    {
        await _page.GotoAsync("/reports", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    }

    /// <summary>
    /// Waits for the page to finish loading.
    /// </summary>
    public async Task WaitForLoadAsync()
    {
        // Wait for the page title to appear first
        await PageTitle.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        // Wait for loading indicator to disappear (passes immediately if it is not shown)
        await Expect(LoadingIndicator).Not.ToBeVisibleAsync(new() { Timeout = 5000 });

        // Wait for filters to be visible (indicates page is loaded)
        await Expect(Filters).ToBeVisibleAsync();
    }

    /// <summary>The page title ("Reports Queue").</summary>
    public ILocator PageTitle => _page.Locator(PageTitleSelector);

    /// <summary>The filter bar (type and status selects).</summary>
    public ILocator Filters => _page.Locator(FilterPaper);

    /// <summary>The page's linear loading indicator.</summary>
    public ILocator LoadingIndicator => _page.Locator(LoadingIndicatorSelector);

    /// <summary>
    /// Selects a type filter option from the MudSelect dropdown.
    /// Uses stable ID to find the parent MudSelect container.
    /// </summary>
    public async Task SelectTypeFilterAsync(string filterOption)
    {
        // Close any existing popovers first
        var existingPopover = _page.Locator(".mud-popover-open");
#pragma warning disable RS0030 // Optional UI: only dismiss a popover that happens to be open; pressing Escape unconditionally is not intended
        var hasOpenPopover = await existingPopover.CountAsync() > 0;
#pragma warning restore RS0030
        if (hasOpenPopover)
        {
            await _page.Keyboard.PressAsync("Escape");
            await Expect(existingPopover).Not.ToBeVisibleAsync(new() { Timeout = 3000 });
        }

        // MudBlazor renders hidden input for form, but visual container is clickable
        // Find the MudSelect by its input-control wrapper that contains the hidden input
        var typeSelectContainer = _page.Locator(".mud-input-control:has(#type-filter)");
        await Expect(typeSelectContainer).ToBeVisibleAsync(new() { Timeout = 5000 });
        await typeSelectContainer.ClickAsync();

        // Wait for popover to appear
        var popover = _page.Locator(".mud-popover-open");

        // Retry click if popover doesn't appear
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await Expect(popover).ToBeVisibleAsync(new() { Timeout = 2000 });
                break;
            }
            catch (PlaywrightException) when (attempt < 2)
            {
                // Popover didn't open, try clicking again
                await typeSelectContainer.ClickAsync();
            }
        }

        // Final check - popover must be visible
        await Expect(popover).ToBeVisibleAsync(new() { Timeout = 3000 });

        // MudBlazor renders options as .mud-list-item inside .mud-popover-open
        var option = popover.Locator(".mud-list-item-clickable").Filter(new() { HasText = filterOption });
        await Expect(option).ToBeVisibleAsync(new() { Timeout = 5000 });
        await option.ClickAsync();

        // Wait for popover to close
        await Expect(popover).Not.ToBeVisibleAsync(new() { Timeout = 5000 });
    }

    /// <summary>
    /// Selects a status filter option from the MudSelect dropdown.
    /// Uses stable ID to find the parent MudSelect container.
    /// </summary>
    public async Task SelectStatusFilterAsync(string filterOption)
    {
        // Close any existing popovers first
        var existingPopover = _page.Locator(".mud-popover-open");
#pragma warning disable RS0030 // Optional UI: only dismiss a popover that happens to be open; pressing Escape unconditionally is not intended
        var hasOpenPopover = await existingPopover.CountAsync() > 0;
#pragma warning restore RS0030
        if (hasOpenPopover)
        {
            await _page.Keyboard.PressAsync("Escape");
            await Expect(existingPopover).Not.ToBeVisibleAsync(new() { Timeout = 3000 });
        }

        // MudBlazor renders hidden input for form, but visual container is clickable
        var statusSelectContainer = _page.Locator(".mud-input-control:has(#status-filter)");
        await Expect(statusSelectContainer).ToBeVisibleAsync(new() { Timeout = 5000 });
        await statusSelectContainer.ClickAsync();

        // Wait for popover to appear
        var popover = _page.Locator(".mud-popover-open");

        // Retry click if popover doesn't appear
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await Expect(popover).ToBeVisibleAsync(new() { Timeout = 2000 });
                break;
            }
            catch (PlaywrightException) when (attempt < 2)
            {
                await statusSelectContainer.ClickAsync();
            }
        }

        await Expect(popover).ToBeVisibleAsync(new() { Timeout = 3000 });

        // MudBlazor renders options as .mud-list-item inside .mud-popover-open
        var option = popover.Locator(".mud-list-item-clickable").Filter(new() { HasText = filterOption });
        await Expect(option).ToBeVisibleAsync(new() { Timeout = 5000 });
        await option.ClickAsync();

        // Wait for popover to close
        await Expect(popover).Not.ToBeVisibleAsync(new() { Timeout = 5000 });
    }

    /// <summary>
    /// Clicks the Refresh button.
    /// </summary>
    public async Task ClickRefreshAsync()
    {
        var refreshButton = _page.Locator(RefreshButton);
        await refreshButton.ClickAsync();
        await WaitForLoadAsync();
    }

    /// <summary>
    /// The pending moderation count chip ("N Moderation"). Only rendered when N &gt; 0.
    /// </summary>
    public ILocator PendingModerationChip => _page.Locator(PendingModerationChipSelector);

    /// <summary>
    /// The pending impersonation count chip ("N Impersonation"). Only rendered when N &gt; 0.
    /// </summary>
    public ILocator PendingImpersonationChip => _page.Locator(PendingImpersonationChipSelector);

    /// <summary>
    /// The pending exam count chip ("N Exam"). Only rendered when N &gt; 0.
    /// </summary>
    public ILocator PendingExamChip => _page.Locator(PendingExamChipSelector);

    /// <summary>
    /// Matches a pending count chip's text when it shows a count of at least 1, e.g. "3 Moderation".
    /// </summary>
    public static Regex PendingCountAtLeastOne(string label) => new($@"^\s*[1-9]\d*\s+{Regex.Escape(label)}");

    /// <summary>The "Moderation Report" card headers, one per moderation report card.</summary>
    public ILocator ModerationReportHeaders =>
        _page.GetByText("Moderation Report", new PageGetByTextOptions { Exact = true });

    /// <summary>The "Impersonation Alert" card headers, one per impersonation alert card.</summary>
    public ILocator ImpersonationAlertHeaders =>
        _page.GetByText("Impersonation Alert", new PageGetByTextOptions { Exact = true });

    /// <summary>The "Exam Review" card headers, one per exam review card.</summary>
    public ILocator ExamReviewHeaders =>
        _page.GetByText("Exam Review", new PageGetByTextOptions { Exact = true });

    /// <summary>
    /// Moderation report and impersonation alert card headers (exam reviews excluded), in document order.
    /// </summary>
    public ILocator DisplayedReportHeaders => ModerationReportHeaders.Or(ImpersonationAlertHeaders);

    /// <summary>
    /// Any report card title text (moderation, impersonation or exam review). Use <c>.First</c>
    /// to assert that at least one report is displayed.
    /// </summary>
    public ILocator AnyReportTitle => _page.GetByText("Moderation Report")
        .Or(_page.GetByText("Impersonation Alert"))
        .Or(_page.GetByText("Exam Review"));

    /// <summary>
    /// Returns the number of moderation report and impersonation alert cards currently displayed.
    /// Callers must first sync on the rendered list with an <c>Expect</c>; this is a one-shot read
    /// for tests that compare counts across filter changes.
    /// </summary>
    public async Task<int> GetDisplayedReportCountAsync()
    {
#pragma warning disable RS0030 // Count feeds a before/after comparison across filters; callers sync with Expect first
        return await DisplayedReportHeaders.CountAsync();
#pragma warning restore RS0030
    }

    /// <summary>The empty-state heading ("No reports found").</summary>
    public ILocator EmptyState => _page.GetByText("No reports found");

    /// <summary>The "All reports have been reviewed!" empty-state message (pending filter).</summary>
    public ILocator AllReviewedMessage => _page.GetByText("All reports have been reviewed!");

    /// <summary>The "No reports match the selected filters." empty-state message.</summary>
    public ILocator NoMatchingFiltersMessage => _page.GetByText("No reports match the selected filters.");

    /// <summary>
    /// The type filter's input. MudSelect stores the display text in the input element.
    /// </summary>
    public ILocator TypeFilterInput => _page.GetByLabel("Type");

    /// <summary>
    /// The status filter's input. MudSelect stores the display text in the input element.
    /// </summary>
    public ILocator StatusFilterInput => _page.GetByLabel("Status");

    #region Report Action Methods

    /// <summary>
    /// Clicks the "Delete as Spam" button on a moderation report card.
    /// This is a NO-CONFIRMATION action that immediately processes the report.
    /// </summary>
    public async Task ClickDeleteAsSpamAsync()
    {
        var button = _page.Locator("button:has-text('Delete as Spam')").First;

        // Wait for Blazor SignalR circuit to be fully established before clicking
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await button.ClickAsync();
    }

    /// <summary>
    /// Clicks the "Ban User" button on a moderation report card.
    /// This is a NO-CONFIRMATION action that immediately bans the user.
    /// </summary>
    public async Task ClickBanUserAsync()
    {
        var button = _page.Locator("button:has-text('Ban User')").First;

        // Wait for Blazor SignalR circuit to be fully established before clicking
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await button.ClickAsync();
    }

    /// <summary>
    /// Clicks the "Warn" button on a moderation report card.
    /// This is a NO-CONFIRMATION action that immediately issues a warning.
    /// </summary>
    public async Task ClickWarnAsync()
    {
        var button = _page.Locator("button:has-text('Warn')").First;

        // Wait for Blazor SignalR circuit to be fully established before clicking
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await button.ClickAsync();
    }

    /// <summary>
    /// Clicks the "Dismiss" button on a report card.
    /// This is a NO-CONFIRMATION action that immediately dismisses the report.
    /// Works for both moderation reports and impersonation alerts.
    /// </summary>
    public async Task ClickDismissAsync()
    {
        var button = _page.Locator("button:has-text('Dismiss')").First;

        // Wait for Blazor SignalR circuit to be fully established before clicking
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await button.ClickAsync();
    }

    /// <summary>
    /// Clicks the "Confirm" button on an impersonation alert card.
    /// This is a NO-CONFIRMATION action.
    /// </summary>
    public async Task ClickConfirmAsync()
    {
        var button = _page.Locator("button:has-text('Confirm')").First;

        // Wait for Blazor SignalR circuit to be fully established before clicking
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await button.ClickAsync();
    }

    /// <summary>
    /// Clicks the "Trust" button on an impersonation alert card.
    /// This is a NO-CONFIRMATION action that trusts/whitelists the user.
    /// </summary>
    public async Task ClickTrustAsync()
    {
        var button = _page.Locator("button:has-text('Trust')").First;

        // Wait for Blazor SignalR circuit to be fully established before clicking
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await button.ClickAsync();
    }

    /// <summary>
    /// Clicks the "Approve" button on an exam review card.
    /// This is a NO-CONFIRMATION action that approves the user's exam.
    /// </summary>
    public async Task ClickApproveExamAsync()
    {
        // Scope to the exam review card to avoid clicking wrong button
        var examCard = _page.Locator(".mud-card:has-text('Exam Review')").First;
        await Expect(examCard).ToBeVisibleAsync(new() { Timeout = 5000 });

        var button = examCard.Locator("button:has-text('Approve')");
        await Expect(button).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Expect(button).ToBeEnabledAsync(new() { Timeout = 5000 });

        // Wait for Blazor SignalR circuit to be fully established
        // Network idle indicates all initial requests (including SignalR) are complete
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await button.ClickAsync();
    }

    /// <summary>
    /// Clicks the "Deny" button on an exam review card.
    /// This is a NO-CONFIRMATION action that denies the user's exam.
    /// </summary>
    public async Task ClickDenyExamAsync()
    {
        // Scope to the exam review card
        var examCard = _page.Locator(".mud-card:has-text('Exam Review')").First;
        await Expect(examCard).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Find the Deny button (exact match, not "Deny + Ban")
        var denyButton = examCard.GetByRole(AriaRole.Button, new() { Name = "Deny", Exact = true });
        await Expect(denyButton).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Expect(denyButton).ToBeEnabledAsync(new() { Timeout = 5000 });

        // Wait for Blazor SignalR circuit to be fully established
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await denyButton.ClickAsync();
    }

    /// <summary>
    /// Clicks the "Deny + Ban" button on an exam review card.
    /// This is a NO-CONFIRMATION action that denies and bans the user.
    /// </summary>
    public async Task ClickDenyAndBanExamAsync()
    {
        // Scope to the exam review card
        var examCard = _page.Locator(".mud-card:has-text('Exam Review')").First;
        await Expect(examCard).ToBeVisibleAsync(new() { Timeout = 5000 });

        var button = examCard.Locator("button:has-text('Deny + Ban')");
        await Expect(button).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Expect(button).ToBeEnabledAsync(new() { Timeout = 5000 });

        // Wait for Blazor SignalR circuit to be fully established
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await button.ClickAsync();
    }

    /// <summary>The MudBlazor snackbar that confirms a report action.</summary>
    public ILocator Snackbar => _page.Locator(".mud-snackbar");

    /// <summary>All action buttons across the displayed report cards.</summary>
    public ILocator ActionButtons => _page.Locator(".mud-card-actions button");

    /// <summary>
    /// The report card action buttons whose text matches <paramref name="text"/>
    /// (pass <see cref="RegexOptions.IgnoreCase"/> for a case-insensitive match).
    /// </summary>
    public ILocator ActionButtonsMatching(Regex text) => ActionButtons.Filter(new() { HasTextRegex = text });

    #endregion

    #region Exam Review Card Methods

    /// <summary>The "Multiple Choice" section of an exam review card.</summary>
    public ILocator ExamMcSection => _page.GetByText("Multiple Choice");

    /// <summary>The "Open-Ended Question" section of an exam review card.</summary>
    public ILocator ExamOpenEndedSection => _page.GetByText("Open-Ended Question");

    /// <summary>The "AI Evaluation" section of an exam review card.</summary>
    public ILocator ExamAiEvaluation => _page.GetByText("AI Evaluation");

    /// <summary>
    /// The score chip of the first exam review card, displayed as "X/Y correct (Z%)".
    /// </summary>
    public ILocator ExamScoreChip => _page.Locator(".mud-chip").Filter(new() { HasText = "correct" }).First;

    /// <summary>The "Passed" status chip of an exam's MC section.</summary>
    public ILocator ExamMcPassedChip => _page.Locator(".mud-chip:has-text('Passed')");

    /// <summary>The "Failed" status chip of an exam's MC section.</summary>
    public ILocator ExamMcFailedChip => _page.Locator(".mud-chip:has-text('Failed')");

    #endregion
}
