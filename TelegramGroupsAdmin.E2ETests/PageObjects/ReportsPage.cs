using Microsoft.Playwright;

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

    // Report card selectors - Both use MudCard component
    private const string ModerationReportCard = ".mud-card:has-text('Moderation Report')";
    private const string ImpersonationAlertCard = ".mud-card:has-text('Impersonation Alert')";

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

        // Wait for loading indicator to disappear (if it appears)
        var loadingIndicator = _page.Locator(LoadingIndicator);
        try
        {
            await loadingIndicator.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Hidden,
                Timeout = 5000
            });
        }
        catch (TimeoutException)
        {
            // Loading indicator may have already disappeared
        }

        // Brief wait for any async content to render
        await _page.WaitForTimeoutAsync(500);
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
        // MudBlazor has nested .mud-select divs, use .First to get outer container
        var typeSelect = _page.Locator(".mud-select:has(#type-filter)").First;
        await typeSelect.ClickAsync();

        // MudBlazor renders options as .mud-list-item inside .mud-popover-open
        var option = _page.Locator($".mud-popover-open .mud-list-item-clickable:has-text('{filterOption}')");
        await option.ClickAsync();

        // Brief wait for filter to apply
        await _page.WaitForTimeoutAsync(300);
    }

    /// <summary>
    /// Selects a status filter option from the MudSelect dropdown.
    /// Uses stable ID to find the parent MudSelect container.
    /// </summary>
    public async Task SelectStatusFilterAsync(string filterOption)
    {
        // MudBlazor has nested .mud-select divs, use .First to get outer container
        var statusSelect = _page.Locator(".mud-select:has(#status-filter)").First;
        await statusSelect.ClickAsync();

        // MudBlazor renders options as .mud-list-item inside .mud-popover-open
        var option = _page.Locator($".mud-popover-open .mud-list-item-clickable:has-text('{filterOption}')");
        await option.ClickAsync();

        // Brief wait for filter to apply
        await _page.WaitForTimeoutAsync(300);
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
        // Check for either type of report card by looking for their header text
        var moderationCard = _page.GetByText("Moderation Report");
        var impersonationCard = _page.GetByText("Impersonation Alert");

        var hasModerationReports = await moderationCard.CountAsync() > 0;
        var hasImpersonationAlerts = await impersonationCard.CountAsync() > 0;

        return hasModerationReports || hasImpersonationAlerts;
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
}
