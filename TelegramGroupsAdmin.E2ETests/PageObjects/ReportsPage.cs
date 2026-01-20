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
    private const string PageTitle = ".mud-typography-h4";
    private const string LoadingIndicator = ".mud-progress-linear";
    private const string FilterPaper = ".mud-paper.pa-3.mb-4";
    private const string TypeFilterSelect = "label:has-text('Type')";
    private const string StatusFilterSelect = "label:has-text('Status')";
    private const string RefreshButton = "button:has-text('Refresh')";
    private const string PendingModerationChip = ".mud-chip:has-text('Moderation')";
    private const string PendingImpersonationChip = ".mud-chip:has-text('Impersonation')";
    private const string ReportCards = ".mud-stack .mud-card";
    private const string EmptyStateIcon = ".mud-icon-root.mud-success-text";

    // Report card selectors - All use MudCard component
    private const string ModerationReportCard = ".mud-card:has-text('Moderation Report')";
    private const string ImpersonationAlertCard = ".mud-card:has-text('Impersonation Alert')";
    private const string ExamReviewCard = ".mud-card:has-text('Exam Review')";
    private const string PendingExamChip = ".mud-chip:has-text('Exam')";

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
    }

    /// <summary>
    /// Waits for the page to finish loading.
    /// </summary>
    public async Task WaitForLoadAsync()
    {
        // Wait for the page title to appear first
        await _page.Locator(PageTitle).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        // Wait for loading indicator to disappear (if it's visible)
        var loadingIndicator = _page.Locator(LoadingIndicator);
        if (await loadingIndicator.IsVisibleAsync())
        {
            await loadingIndicator.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Hidden,
                Timeout = 5000
            });
        }

        // Wait for filters to be visible (indicates page is loaded)
        await Expect(_page.Locator(FilterPaper)).ToBeVisibleAsync();
    }

    /// <summary>
    /// Returns true if the page title is visible.
    /// </summary>
    public async Task<bool> IsPageTitleVisibleAsync()
    {
        var title = _page.Locator(PageTitle);
        return await title.IsVisibleAsync();
    }

    /// <summary>
    /// Gets the page title text.
    /// </summary>
    public async Task<string> GetPageTitleAsync()
    {
        var title = _page.Locator(PageTitle);
        return await title.TextContentAsync() ?? "";
    }

    /// <summary>
    /// Returns true if filters are visible.
    /// </summary>
    public async Task<bool> AreFiltersVisibleAsync()
    {
        var filterPaper = _page.Locator(FilterPaper);
        return await filterPaper.IsVisibleAsync();
    }

    /// <summary>
    /// Selects a type filter option from the MudSelect dropdown.
    /// Uses stable ID to find the parent MudSelect container.
    /// </summary>
    public async Task SelectTypeFilterAsync(string filterOption)
    {
        // Close any existing popovers first
        var existingPopover = _page.Locator(".mud-popover-open");
        if (await existingPopover.CountAsync() > 0)
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
        if (await existingPopover.CountAsync() > 0)
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
    /// Returns true if the pending moderation chip is visible.
    /// </summary>
    public async Task<bool> IsPendingModerationChipVisibleAsync()
    {
        var chip = _page.Locator(PendingModerationChip);
        return await chip.IsVisibleAsync();
    }

    /// <summary>
    /// Returns true if the pending impersonation chip is visible.
    /// </summary>
    public async Task<bool> IsPendingImpersonationChipVisibleAsync()
    {
        var chip = _page.Locator(PendingImpersonationChip);
        return await chip.IsVisibleAsync();
    }

    /// <summary>
    /// Gets the pending moderation count from the chip.
    /// </summary>
    public async Task<int> GetPendingModerationCountAsync()
    {
        var chip = _page.Locator(PendingModerationChip);
        if (!await chip.IsVisibleAsync())
            return 0;

        var text = await chip.TextContentAsync() ?? "";
        // Extract number from "X Moderation"
        var parts = text.Trim().Split(' ');
        if (parts.Length > 0 && int.TryParse(parts[0], out var count))
            return count;
        return 0;
    }

    /// <summary>
    /// Gets the pending impersonation count from the chip.
    /// </summary>
    public async Task<int> GetPendingImpersonationCountAsync()
    {
        var chip = _page.Locator(PendingImpersonationChip);
        if (!await chip.IsVisibleAsync())
            return 0;

        var text = await chip.TextContentAsync() ?? "";
        // Extract number from "X Impersonation"
        var parts = text.Trim().Split(' ');
        if (parts.Length > 0 && int.TryParse(parts[0], out var count))
            return count;
        return 0;
    }

    /// <summary>
    /// Returns the total count of report cards displayed.
    /// </summary>
    public async Task<int> GetDisplayedReportCountAsync()
    {
        var moderationCount = await GetModerationReportCountAsync();
        var impersonationCount = await GetImpersonationAlertCountAsync();
        return moderationCount + impersonationCount;
    }

    /// <summary>
    /// Returns the count of moderation report cards.
    /// </summary>
    public async Task<int> GetModerationReportCountAsync()
    {
        // Find card headers with "Moderation Report" text
        var cards = _page.GetByText("Moderation Report", new PageGetByTextOptions { Exact = true });
        return await cards.CountAsync();
    }

    /// <summary>
    /// Returns the count of impersonation alert cards.
    /// </summary>
    public async Task<int> GetImpersonationAlertCountAsync()
    {
        // Find card headers with "Impersonation Alert" text
        var cards = _page.GetByText("Impersonation Alert", new PageGetByTextOptions { Exact = true });
        return await cards.CountAsync();
    }

    /// <summary>
    /// Returns the count of exam review cards.
    /// </summary>
    public async Task<int> GetExamReviewCountAsync()
    {
        // Find card headers with "Exam Review" text
        var cards = _page.GetByText("Exam Review", new PageGetByTextOptions { Exact = true });
        return await cards.CountAsync();
    }

    /// <summary>
    /// Returns true if the pending exam chip is visible.
    /// </summary>
    public async Task<bool> IsPendingExamChipVisibleAsync()
    {
        var chip = _page.Locator(PendingExamChip);
        return await chip.IsVisibleAsync();
    }

    /// <summary>
    /// Gets the pending exam count from the chip.
    /// </summary>
    public async Task<int> GetPendingExamCountAsync()
    {
        var chip = _page.Locator(PendingExamChip);
        if (!await chip.IsVisibleAsync())
            return 0;

        var text = await chip.TextContentAsync() ?? "";
        // Extract number from "X Exam"
        var parts = text.Trim().Split(' ');
        if (parts.Length > 0 && int.TryParse(parts[0], out var count))
            return count;
        return 0;
    }

    /// <summary>
    /// Returns true if the empty state is visible.
    /// </summary>
    public async Task<bool> IsEmptyStateVisibleAsync()
    {
        var emptyState = _page.GetByText("No reports found");
        return await emptyState.IsVisibleAsync();
    }

    /// <summary>
    /// Returns true if the "All reports have been reviewed!" message is visible.
    /// </summary>
    public async Task<bool> IsAllReviewedMessageVisibleAsync()
    {
        var message = _page.GetByText("All reports have been reviewed!");
        return await message.IsVisibleAsync();
    }

    /// <summary>
    /// Returns true if the "No reports match the selected filters." message is visible.
    /// </summary>
    public async Task<bool> IsNoMatchingFiltersMessageVisibleAsync()
    {
        var message = _page.GetByText("No reports match the selected filters.");
        return await message.IsVisibleAsync();
    }

    /// <summary>
    /// Returns true if any report cards are displayed.
    /// </summary>
    public async Task<bool> HasReportsAsync()
    {
        // Check for any type of report card by looking for their header text
        var moderationCard = _page.GetByText("Moderation Report");
        var impersonationCard = _page.GetByText("Impersonation Alert");
        var examCard = _page.GetByText("Exam Review");

        var hasModerationReports = await moderationCard.CountAsync() > 0;
        var hasImpersonationAlerts = await impersonationCard.CountAsync() > 0;
        var hasExamReviews = await examCard.CountAsync() > 0;

        return hasModerationReports || hasImpersonationAlerts || hasExamReviews;
    }

    /// <summary>
    /// Gets the type filter dropdown's current selected value.
    /// MudSelect stores the display text in the input element.
    /// </summary>
    public async Task<string> GetSelectedTypeFilterAsync()
    {
        var typeSelect = _page.GetByLabel("Type");
        return await typeSelect.InputValueAsync();
    }

    /// <summary>
    /// Gets the status filter dropdown's current selected value.
    /// MudSelect stores the display text in the input element.
    /// </summary>
    public async Task<string> GetSelectedStatusFilterAsync()
    {
        var statusSelect = _page.GetByLabel("Status");
        return await statusSelect.InputValueAsync();
    }

    /// <summary>
    /// Returns true if the page is currently loading.
    /// </summary>
    public async Task<bool> IsLoadingAsync()
    {
        var loadingIndicator = _page.Locator(LoadingIndicator);
        return await loadingIndicator.IsVisibleAsync();
    }

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
    /// Clicks the "Dismiss" button on a moderation report card.
    /// This is a NO-CONFIRMATION action that immediately dismisses the report.
    /// Note: For impersonation alerts, use ClickFalsePositiveAsync instead.
    /// </summary>
    public async Task ClickDismissAsync()
    {
        var button = _page.Locator("button:has-text('Dismiss')").First;

        // Wait for Blazor SignalR circuit to be fully established before clicking
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await button.ClickAsync();
    }

    /// <summary>
    /// Clicks the "Confirm Scam" button on an impersonation alert card.
    /// This is a NO-CONFIRMATION action.
    /// </summary>
    public async Task ClickConfirmScamAsync()
    {
        var button = _page.Locator("button:has-text('Confirm Scam')").First;

        // Wait for Blazor SignalR circuit to be fully established before clicking
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await button.ClickAsync();
    }

    /// <summary>
    /// Clicks the "False Positive" button on an impersonation alert card.
    /// This is a NO-CONFIRMATION action that dismisses the alert.
    /// </summary>
    public async Task ClickFalsePositiveAsync()
    {
        var button = _page.Locator("button:has-text('False Positive')").First;

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

    /// <summary>
    /// Waits for a snackbar message to appear and returns its text.
    /// </summary>
    public async Task<string?> WaitForSnackbarAsync(int timeoutMs = 5000)
    {
        var snackbar = _page.Locator(".mud-snackbar");
        await snackbar.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = timeoutMs
        });
        return await snackbar.TextContentAsync();
    }

    /// <summary>
    /// Returns true if any action button is visible on the first report card.
    /// </summary>
    public async Task<bool> HasActionButtonsVisibleAsync()
    {
        var actionButtons = _page.Locator(".mud-card-actions button");
        return await actionButtons.CountAsync() > 0;
    }

    /// <summary>
    /// Gets all visible action button texts.
    /// </summary>
    public async Task<List<string>> GetVisibleActionButtonsAsync()
    {
        var buttons = new List<string>();
        var actionButtons = await _page.Locator(".mud-card-actions button").AllAsync();
        foreach (var button in actionButtons)
        {
            var text = await button.TextContentAsync();
            if (!string.IsNullOrWhiteSpace(text))
                buttons.Add(text.Trim());
        }
        return buttons;
    }

    #endregion

    #region Exam Review Card Methods

    /// <summary>
    /// Waits for and returns true if exam review cards show Multiple Choice section.
    /// Uses web-first assertion pattern for Blazor compatibility.
    /// </summary>
    public async Task<bool> HasExamMcSectionAsync()
    {
        try
        {
            var mcSection = _page.GetByText("Multiple Choice");
            await Expect(mcSection).ToBeVisibleAsync(new() { Timeout = 5000 });
            return true;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    /// <summary>
    /// Waits for and returns true if exam review cards show Open-Ended Question section.
    /// Uses web-first assertion pattern for Blazor compatibility.
    /// </summary>
    public async Task<bool> HasExamOpenEndedSectionAsync()
    {
        try
        {
            var openEndedSection = _page.GetByText("Open-Ended Question");
            await Expect(openEndedSection).ToBeVisibleAsync(new() { Timeout = 5000 });
            return true;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    /// <summary>
    /// Waits for and returns true if exam review cards show AI Evaluation section.
    /// Uses web-first assertion pattern for Blazor compatibility.
    /// </summary>
    public async Task<bool> HasExamAiEvaluationAsync()
    {
        try
        {
            var aiEval = _page.GetByText("AI Evaluation");
            await Expect(aiEval).ToBeVisibleAsync(new() { Timeout = 5000 });
            return true;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the score displayed in the first exam review card.
    /// Returns null if no score found.
    /// </summary>
    public async Task<string?> GetExamScoreAsync()
    {
        try
        {
            // Score is displayed as "X/Y correct (Z%)"
            var scoreElement = _page.Locator(".mud-chip").Filter(new() { HasText = "correct" }).First;
            await Expect(scoreElement).ToBeVisibleAsync(new() { Timeout = 5000 });
            return await scoreElement.TextContentAsync();
        }
        catch (PlaywrightException)
        {
            return null;
        }
    }

    /// <summary>
    /// Waits for and returns true if the exam shows a "Passed" status for MC section.
    /// Uses web-first assertion pattern for Blazor compatibility.
    /// </summary>
    public async Task<bool> IsExamMcPassedAsync()
    {
        try
        {
            var passedChip = _page.Locator(".mud-chip:has-text('Passed')");
            await Expect(passedChip).ToBeVisibleAsync(new() { Timeout = 5000 });
            return true;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    /// <summary>
    /// Waits for and returns true if the exam shows a "Failed" status for MC section.
    /// Uses web-first assertion pattern for Blazor compatibility.
    /// </summary>
    public async Task<bool> IsExamMcFailedAsync()
    {
        try
        {
            var failedChip = _page.Locator(".mud-chip:has-text('Failed')");
            await Expect(failedChip).ToBeVisibleAsync(new() { Timeout = 5000 });
            return true;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    #endregion
}
