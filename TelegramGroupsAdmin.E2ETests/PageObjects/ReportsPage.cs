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
    private const string RefreshButton = "button:has-text('Refresh')";
    private const string PendingModerationChipSelector = ".mud-chip:has-text('Moderation')";
    private const string PendingImpersonationChipSelector = ".mud-chip:has-text('Impersonation')";

    // Report card selectors - All use MudCard component
    private const string ExamReviewCard = ".mud-card:has-text('Exam Review')";
    private const string ModerationReportCard = ".mud-card:has-text('Moderation Report')";
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
        await _page.GotoAsync("/reports");
        await _page.WaitForInteractiveAsync();
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
        // A click on the prerendered select has no handler and never opens the popover
        await _page.WaitForInteractiveAsync();
        await typeSelectContainer.ClickAsync();

        var popover = _page.Locator(".mud-popover-open");
        await Expect(popover).ToBeVisibleAsync(new() { Timeout = 5000 });

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
        // A click on the prerendered select has no handler and never opens the popover
        await _page.WaitForInteractiveAsync();
        await statusSelectContainer.ClickAsync();

        var popover = _page.Locator(".mud-popover-open");
        await Expect(popover).ToBeVisibleAsync(new() { Timeout = 5000 });

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

    /// <summary>The exam review cards (MudCards containing an "Exam Review" header).</summary>
    public ILocator ExamReviewCards => _page.Locator(ExamReviewCard);

    /// <summary>The moderation report cards (MudCards containing a "Moderation Report" header).</summary>
    public ILocator ModerationReportCards => _page.Locator(ModerationReportCard);

    /// <summary>
    /// The moderation report card for the given reported Telegram user, matched on the card's
    /// "ID: &lt;id&gt;" caption (rendered when the reported user has a name or username).
    /// No <c>\b</c> anchors: they do not survive the hand-off to Playwright's regex engine.
    /// </summary>
    public ILocator ModerationReportCardFor(long reportedUserId) =>
        ModerationReportCards.Filter(new() { HasTextRegex = new Regex($@"ID:\s*{reportedUserId}(?!\d)") });

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

    #region Report Action Methods

    /// <summary>
    /// Clicks the "Delete as Spam" button on a moderation report card.
    /// This is a NO-CONFIRMATION action that immediately processes the report.
    /// </summary>
    public async Task ClickDeleteAsSpamAsync()
    {
        var button = _page.Locator("button:has-text('Delete as Spam')").First;

        // A click on the prerendered button has no handler; wait for the live circuit first
        await _page.WaitForInteractiveAsync();

        await button.ClickAsync();
    }

    /// <summary>
    /// Clicks the "Ban User" button on a moderation report card.
    /// This is a NO-CONFIRMATION action that immediately bans the user.
    /// </summary>
    public async Task ClickBanUserAsync()
    {
        var button = _page.Locator("button:has-text('Ban User')").First;

        // A click on the prerendered button has no handler; wait for the live circuit first
        await _page.WaitForInteractiveAsync();

        await button.ClickAsync();
    }

    /// <summary>
    /// Clicks the "Warn" button inside <paramref name="card"/> (a moderation report card, e.g.
    /// <see cref="ModerationReportCardFor"/>), so a test with several pending reports acts on the one it means.
    /// </summary>
    public async Task ClickWarnInCardAsync(ILocator card)
    {
        var button = card.GetByRole(AriaRole.Button, new() { Name = "Warn", Exact = true });
        await Expect(button).ToBeEnabledAsync(new() { Timeout = 5000 });

        // A click on the prerendered button has no handler; wait for the live circuit first
        await _page.WaitForInteractiveAsync();

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

        // A click on the prerendered button has no handler; wait for the live circuit first
        await _page.WaitForInteractiveAsync();

        await button.ClickAsync();
    }

    /// <summary>
    /// Clicks the "Confirm" button on an impersonation alert card.
    /// This is a NO-CONFIRMATION action.
    /// </summary>
    public async Task ClickConfirmAsync()
    {
        var button = _page.Locator("button:has-text('Confirm')").First;

        // A click on the prerendered button has no handler; wait for the live circuit first
        await _page.WaitForInteractiveAsync();

        await button.ClickAsync();
    }

    /// <summary>
    /// Clicks the "Trust" button on an impersonation alert card.
    /// This is a NO-CONFIRMATION action that trusts/whitelists the user.
    /// </summary>
    public async Task ClickTrustAsync()
    {
        var button = _page.Locator("button:has-text('Trust')").First;

        // A click on the prerendered button has no handler; wait for the live circuit first
        await _page.WaitForInteractiveAsync();

        await button.ClickAsync();
    }

    /// <summary>
    /// Clicks the "Approve" button on an exam review card.
    /// This is a NO-CONFIRMATION action that approves the user's exam.
    /// </summary>
    public async Task ClickApproveExamAsync()
    {
        // Scope to the exam review card to avoid clicking wrong button
        var examCard = ExamReviewCards.First;
        await Expect(examCard).ToBeVisibleAsync(new() { Timeout = 5000 });

        var button = examCard.Locator("button:has-text('Approve')");
        await Expect(button).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Expect(button).ToBeEnabledAsync(new() { Timeout = 5000 });

        // A click on the prerendered button has no handler; wait for the live circuit first
        await _page.WaitForInteractiveAsync();

        await button.ClickAsync();
    }

    /// <summary>
    /// Clicks the "Deny" button on an exam review card.
    /// This is a NO-CONFIRMATION action that denies the user's exam.
    /// </summary>
    public async Task ClickDenyExamAsync()
    {
        // Scope to the exam review card
        var examCard = ExamReviewCards.First;
        await Expect(examCard).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Find the Deny button (exact match, not "Deny + Ban")
        var denyButton = examCard.GetByRole(AriaRole.Button, new() { Name = "Deny", Exact = true });
        await Expect(denyButton).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Expect(denyButton).ToBeEnabledAsync(new() { Timeout = 5000 });

        // A click on the prerendered button has no handler; wait for the live circuit first
        await _page.WaitForInteractiveAsync();

        await denyButton.ClickAsync();
    }

    /// <summary>
    /// Clicks the "Deny + Ban" button on an exam review card.
    /// This is a NO-CONFIRMATION action that denies and bans the user.
    /// </summary>
    public async Task ClickDenyAndBanExamAsync()
    {
        // Scope to the exam review card
        var examCard = ExamReviewCards.First;
        await Expect(examCard).ToBeVisibleAsync(new() { Timeout = 5000 });

        var button = examCard.Locator("button:has-text('Deny + Ban')");
        await Expect(button).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Expect(button).ToBeEnabledAsync(new() { Timeout = 5000 });

        // A click on the prerendered button has no handler; wait for the live circuit first
        await _page.WaitForInteractiveAsync();

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
    /// The header row of an exam review card's "Multiple Choice" section (title, score chip and the
    /// MC Passed/Failed chip). The innermost flex row containing the title, so the card's own header
    /// ("Passed — auto-admitted") and the AI evaluation chip are outside it.
    /// </summary>
    public ILocator ExamMcSectionHeader => ExamMcSectionHeaderIn(_page.Locator("body"));

    /// <summary>The MC section header row within <paramref name="scope"/> (MudStack Row renders <c>d-flex flex-row</c>).</summary>
    public static ILocator ExamMcSectionHeaderIn(ILocator scope) =>
        scope.Locator(".flex-row").Filter(new()
        {
            HasText = "Multiple Choice",
            // Truly innermost: drop any row that itself contains another row with the title.
            HasNot = scope.Page.Locator(".flex-row").Filter(new() { HasText = "Multiple Choice" })
        });

    /// <summary>The MC "Passed" chip, scoped to the Multiple Choice section (not "Passed — auto-admitted" or the AI chip).</summary>
    public ILocator ExamMcPassedChip => ExamMcSectionHeader.Locator(".mud-chip").Filter(new() { HasTextRegex = new Regex(@"^\s*Passed\s*$") });

    /// <summary>The MC "Failed" chip, scoped to the Multiple Choice section (not the AI chip).</summary>
    public ILocator ExamMcFailedChip => ExamMcSectionHeader.Locator(".mud-chip").Filter(new() { HasTextRegex = new Regex(@"^\s*Failed\s*$") });

    #endregion
}
