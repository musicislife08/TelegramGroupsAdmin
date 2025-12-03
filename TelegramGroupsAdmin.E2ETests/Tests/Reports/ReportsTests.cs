using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Reports;

/// <summary>
/// Tests for the Reports Queue page (/reports).
/// Verifies report display, filtering, permission-based access, and pending counts.
/// Note: This page requires GlobalAdmin or Owner role - Admin cannot access.
/// </summary>
[TestFixture]
public class ReportsTests : AuthenticatedTestBase
{
    private ReportsPage _reportsPage = null!;

    [SetUp]
    public void SetUp()
    {
        _reportsPage = new ReportsPage(Page);
    }

    [Test]
    public async Task Reports_LoadsSuccessfully_WhenGlobalAdmin()
    {
        // Arrange - login as GlobalAdmin
        await LoginAsGlobalAdminAsync();

        // Act - navigate to reports page
        await _reportsPage.NavigateAsync();
        await _reportsPage.WaitForLoadAsync();

        // Assert - page title is visible
        Assert.That(await _reportsPage.IsPageTitleVisibleAsync(), Is.True,
            "Reports page title should be visible");

        var pageTitle = await _reportsPage.GetPageTitleAsync();
        Assert.That(pageTitle, Is.EqualTo("Reports Queue"),
            "Page title should be 'Reports Queue'");
    }

    [Test]
    public async Task Reports_LoadsSuccessfully_WhenOwner()
    {
        // Arrange - login as Owner
        await LoginAsOwnerAsync();

        // Act
        await _reportsPage.NavigateAsync();
        await _reportsPage.WaitForLoadAsync();

        // Assert - page title is visible
        Assert.That(await _reportsPage.IsPageTitleVisibleAsync(), Is.True,
            "Owner should be able to view reports page");
    }

    [Test]
    public async Task Reports_RequiresAuthentication()
    {
        // Act - try to access reports without login
        await Page.GotoAsync("/reports");

        // Assert - should redirect to login or register
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/(login|register)"));
    }

    [Test]
    public async Task Reports_DeniesAccess_WhenAdmin()
    {
        // Arrange - login as Admin (lowest role, not allowed on Reports page)
        await LoginAsAdminAsync();

        // Act
        await Page.GotoAsync("/reports");
        await Page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.NetworkIdle);

        // Assert - Admin should be redirected or see access denied
        // The page requires GlobalAdmin or Owner role
        var isOnReportsPage = Page.Url.Contains("/reports");
        var hasAccessDenied = await Page.Locator("text=Access Denied").Or(
            Page.Locator("text=not authorized")).IsVisibleAsync();

        // Either redirected away or shown access denied message
        Assert.That(!isOnReportsPage || hasAccessDenied, Is.True,
            "Admin user should not be able to access Reports page");
    }

    [Test]
    public async Task Reports_DisplaysFilters_WhenAuthenticated()
    {
        // Arrange
        await LoginAsOwnerAsync();

        // Act
        await _reportsPage.NavigateAsync();
        await _reportsPage.WaitForLoadAsync();

        // Assert - filters are visible
        Assert.That(await _reportsPage.AreFiltersVisibleAsync(), Is.True,
            "Filters should be visible on Reports page");
    }

    [Test]
    public async Task Reports_ShowsEmptyState_WhenNoReports()
    {
        // Arrange - fresh database, no reports
        await LoginAsOwnerAsync();

        // Act
        await _reportsPage.NavigateAsync();
        await _reportsPage.WaitForLoadAsync();

        // Assert - empty state message visible
        Assert.That(await _reportsPage.IsEmptyStateVisibleAsync(), Is.True,
            "Empty state should be visible when no reports exist");

        Assert.That(await _reportsPage.IsAllReviewedMessageVisibleAsync(), Is.True,
            "Should show 'All reports have been reviewed!' message for pending filter");
    }

    [Test]
    public async Task Reports_DisplaysModerationReport_WhenPending()
    {
        // Arrange
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(Factory.Services)
            .WithTitle("Test Chat")
            .BuildAsync();

        var message = await new TestMessageBuilder(Factory.Services)
            .InChat(chat)
            .FromUser(111111, "spammer", "Spam", "User")
            .WithText("Spam message content")
            .BuildAsync();

        var report = await new TestReportBuilder(Factory.Services)
            .ForMessage(message)
            .InChat(chat)
            .ReportedBy(222222, "reporter")
            .BuildAsync();

        // Act
        await _reportsPage.NavigateAsync();
        await _reportsPage.WaitForLoadAsync();

        // Assert
        Assert.That(await _reportsPage.HasReportsAsync(), Is.True,
            "Should display reports when pending reports exist");

        Assert.That(await _reportsPage.IsPendingModerationChipVisibleAsync(), Is.True,
            "Pending moderation chip should be visible");

        Assert.That(await _reportsPage.GetPendingModerationCountAsync(), Is.GreaterThanOrEqualTo(1),
            "Should show at least 1 pending moderation report");
    }

    [Test]
    public async Task Reports_DisplaysImpersonationAlert_WhenPending()
    {
        // Arrange
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(Factory.Services)
            .WithTitle("Alert Test Chat")
            .BuildAsync();

        // Create telegram users first (foreign key requirements)
        await new TestTelegramUserBuilder(Factory.Services)
            .WithUserId(333333)
            .WithUsername("scammer")
            .WithName("Scam", "User")
            .BuildAsync();

        await new TestTelegramUserBuilder(Factory.Services)
            .WithUserId(444444)
            .WithUsername("admin")
            .WithName("Real", "Admin")
            .BuildAsync();

        var alert = await new TestImpersonationAlertBuilder(Factory.Services)
            .WithSuspectedUser(333333, "scammer", "Scam", "User")
            .WithTargetUser(444444, "admin", "Real", "Admin")
            .InChat(chat)
            .WithRiskLevel(ImpersonationRiskLevel.Medium)
            .BuildAsync();

        // Act
        await _reportsPage.NavigateAsync();
        await _reportsPage.WaitForLoadAsync();

        // Assert
        Assert.That(await _reportsPage.HasReportsAsync(), Is.True,
            "Should display alerts when pending impersonation alerts exist");

        Assert.That(await _reportsPage.IsPendingImpersonationChipVisibleAsync(), Is.True,
            "Pending impersonation chip should be visible");

        Assert.That(await _reportsPage.GetPendingImpersonationCountAsync(), Is.GreaterThanOrEqualTo(1),
            "Should show at least 1 pending impersonation alert");
    }

    [Test]
    public async Task Reports_FiltersByType_ModerationReports()
    {
        // Arrange
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(Factory.Services)
            .WithTitle("Filter Test Chat")
            .BuildAsync();

        // Create moderation report
        var message = await new TestMessageBuilder(Factory.Services)
            .InChat(chat)
            .FromUser(555555, "user1", "Test", "User")
            .WithText("Reported message")
            .BuildAsync();

        await new TestReportBuilder(Factory.Services)
            .ForMessage(message)
            .InChat(chat)
            .ReportedBy(666666, "reporter")
            .BuildAsync();

        // Create impersonation alert
        await new TestTelegramUserBuilder(Factory.Services)
            .WithUserId(777777)
            .WithUsername("suspected")
            .WithName("Suspected", "User")
            .BuildAsync();

        await new TestTelegramUserBuilder(Factory.Services)
            .WithUserId(888888)
            .WithUsername("target")
            .WithName("Target", "Admin")
            .BuildAsync();

        await new TestImpersonationAlertBuilder(Factory.Services)
            .WithSuspectedUser(777777, "suspected", "Suspected", "User")
            .WithTargetUser(888888, "target", "Target", "Admin")
            .InChat(chat)
            .BuildAsync();

        // Act
        await _reportsPage.NavigateAsync();
        await _reportsPage.WaitForLoadAsync();

        // Filter to Moderation Reports only
        await _reportsPage.SelectTypeFilterAsync("Moderation Reports");

        // Assert - only moderation reports should be visible
        // Use web-first assertions that auto-retry for Blazor re-render
        await Expect(Page.GetByText("Moderation Report", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText("Impersonation Alert", new() { Exact = true })).Not.ToBeVisibleAsync();

        var moderationCount = await _reportsPage.GetModerationReportCountAsync();
        var impersonationCount = await _reportsPage.GetImpersonationAlertCountAsync();

        Assert.That(moderationCount, Is.GreaterThanOrEqualTo(1),
            "Should show moderation reports when filtered");
        Assert.That(impersonationCount, Is.EqualTo(0),
            "Should not show impersonation alerts when filtered to moderation");
    }

    [Test]
    public async Task Reports_FiltersByType_ImpersonationAlerts()
    {
        // Arrange
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(Factory.Services)
            .WithTitle("Impersonation Filter Chat")
            .BuildAsync();

        // Create moderation report
        var message = await new TestMessageBuilder(Factory.Services)
            .InChat(chat)
            .FromUser(111222, "reporter", "Report", "User")
            .WithText("Some message")
            .BuildAsync();

        await new TestReportBuilder(Factory.Services)
            .ForMessage(message)
            .InChat(chat)
            .ReportedBy(333444, "admin")
            .BuildAsync();

        // Create impersonation alert
        await new TestTelegramUserBuilder(Factory.Services)
            .WithUserId(555666)
            .WithUsername("faker")
            .WithName("Fake", "Admin")
            .BuildAsync();

        await new TestTelegramUserBuilder(Factory.Services)
            .WithUserId(777888)
            .WithUsername("realadmin")
            .WithName("Real", "Admin")
            .BuildAsync();

        await new TestImpersonationAlertBuilder(Factory.Services)
            .WithSuspectedUser(555666, "faker", "Fake", "Admin")
            .WithTargetUser(777888, "realadmin", "Real", "Admin")
            .InChat(chat)
            .BuildAsync();

        // Act
        await _reportsPage.NavigateAsync();
        await _reportsPage.WaitForLoadAsync();

        // Filter to Impersonation Alerts only
        await _reportsPage.SelectTypeFilterAsync("Impersonation Alerts");

        // Assert - only impersonation alerts should be visible
        // Use web-first assertions that auto-retry for Blazor re-render
        await Expect(Page.GetByText("Impersonation Alert", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText("Moderation Report", new() { Exact = true })).Not.ToBeVisibleAsync();

        var moderationCount = await _reportsPage.GetModerationReportCountAsync();
        var impersonationCount = await _reportsPage.GetImpersonationAlertCountAsync();

        Assert.That(impersonationCount, Is.GreaterThanOrEqualTo(1),
            "Should show impersonation alerts when filtered");
        Assert.That(moderationCount, Is.EqualTo(0),
            "Should not show moderation reports when filtered to impersonation");
    }

    [Test]
    public async Task Reports_FiltersByStatus_AllStatuses()
    {
        // Arrange
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(Factory.Services)
            .WithTitle("Status Filter Chat")
            .BuildAsync();

        // Create pending report
        var message1 = await new TestMessageBuilder(Factory.Services)
            .InChat(chat)
            .FromUser(100001, "user1", "Pending", "User")
            .WithText("Pending report message")
            .BuildAsync();

        await new TestReportBuilder(Factory.Services)
            .ForMessage(message1)
            .InChat(chat)
            .ReportedBy(100002, "reporter1")
            .BuildAsync();

        // Create reviewed report
        var message2 = await new TestMessageBuilder(Factory.Services)
            .InChat(chat)
            .FromUser(100003, "user2", "Reviewed", "User")
            .WithText("Reviewed report message")
            .BuildAsync();

        await new TestReportBuilder(Factory.Services)
            .ForMessageId((int)message2.MessageId)
            .InChat(chat)
            .ReportedBy(100004, "reporter2")
            .AsReviewed("admin", "Dismissed")
            .BuildAsync();

        // Act
        await _reportsPage.NavigateAsync();
        await _reportsPage.WaitForLoadAsync();

        // Get count with default Pending filter
        var pendingOnlyCount = await _reportsPage.GetDisplayedReportCountAsync();

        // Switch to All Statuses
        await _reportsPage.SelectStatusFilterAsync("All Statuses");

        // Wait for MudSelect to show the new filter value (web-first assertion)
        // MudBlazor displays selected text in div.mud-input-slot (not the hidden input)
        var statusSelectText = Page.Locator(".mud-select:has(#status-filter) div.mud-input-slot");
        await Expect(statusSelectText).ToHaveTextAsync("All Statuses");

        var allStatusesCount = await _reportsPage.GetDisplayedReportCountAsync();

        // Assert - All Statuses should show more or equal reports
        Assert.That(allStatusesCount, Is.GreaterThanOrEqualTo(pendingOnlyCount),
            "All Statuses filter should show at least as many reports as Pending Only");
    }

    [Test]
    public async Task Reports_RefreshButton_ReloadsReports()
    {
        // Arrange
        await LoginAsOwnerAsync();

        // Act
        await _reportsPage.NavigateAsync();
        await _reportsPage.WaitForLoadAsync();

        // Click refresh
        await _reportsPage.ClickRefreshAsync();

        // Assert - page should still be loaded (no errors)
        Assert.That(await _reportsPage.IsPageTitleVisibleAsync(), Is.True,
            "Page should remain loaded after refresh");
    }

    [Test]
    public async Task Reports_CriticalAlert_ShowsHighPriority()
    {
        // Arrange
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(Factory.Services)
            .WithTitle("Critical Alert Chat")
            .BuildAsync();

        // Create telegram users
        await new TestTelegramUserBuilder(Factory.Services)
            .WithUserId(999001)
            .WithUsername("criticalscammer")
            .WithName("Scam", "Artist")
            .BuildAsync();

        await new TestTelegramUserBuilder(Factory.Services)
            .WithUserId(999002)
            .WithUsername("targetadmin")
            .WithName("Target", "Admin")
            .BuildAsync();

        // Create critical impersonation alert
        await new TestImpersonationAlertBuilder(Factory.Services)
            .WithSuspectedUser(999001, "criticalscammer", "Scam", "Artist")
            .WithTargetUser(999002, "targetadmin", "Target", "Admin")
            .InChat(chat)
            .AsCritical()
            .BuildAsync();

        // Act
        await _reportsPage.NavigateAsync();
        await _reportsPage.WaitForLoadAsync();

        // Assert - critical alert should be displayed
        Assert.That(await _reportsPage.HasReportsAsync(), Is.True,
            "Critical alert should be displayed");

        Assert.That(await _reportsPage.IsPendingImpersonationChipVisibleAsync(), Is.True,
            "Pending impersonation chip should be visible for critical alert");
    }

    [Test]
    public async Task Reports_GlobalAdminSeesAllReports()
    {
        // Arrange - GlobalAdmin should see all reports across all chats
        await LoginAsGlobalAdminAsync();

        var chat1 = await new TestChatBuilder(Factory.Services)
            .WithTitle("Chat One")
            .BuildAsync();

        var chat2 = await new TestChatBuilder(Factory.Services)
            .WithTitle("Chat Two")
            .BuildAsync();

        // Create reports in different chats
        var message1 = await new TestMessageBuilder(Factory.Services)
            .InChat(chat1)
            .FromUser(200001, "user1", "User", "One")
            .WithText("Message in chat 1")
            .BuildAsync();

        await new TestReportBuilder(Factory.Services)
            .ForMessage(message1)
            .InChat(chat1)
            .ReportedBy(200002, "reporter1")
            .BuildAsync();

        var message2 = await new TestMessageBuilder(Factory.Services)
            .InChat(chat2)
            .FromUser(200003, "user2", "User", "Two")
            .WithText("Message in chat 2")
            .BuildAsync();

        await new TestReportBuilder(Factory.Services)
            .ForMessage(message2)
            .InChat(chat2)
            .ReportedBy(200004, "reporter2")
            .BuildAsync();

        // Act
        await _reportsPage.NavigateAsync();
        await _reportsPage.WaitForLoadAsync();

        // Assert - GlobalAdmin sees reports from all chats
        var totalCount = await _reportsPage.GetDisplayedReportCountAsync();
        Assert.That(totalCount, Is.GreaterThanOrEqualTo(2),
            "GlobalAdmin should see reports from all chats");
    }

    [Test]
    public async Task Reports_ShowsNoMatchingFilters_WhenFilteredEmpty()
    {
        // Arrange - create only moderation reports, then filter to impersonation
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(Factory.Services)
            .WithTitle("No Match Chat")
            .BuildAsync();

        // Create only moderation report
        var message = await new TestMessageBuilder(Factory.Services)
            .InChat(chat)
            .FromUser(300001, "user", "Test", "User")
            .WithText("Test message")
            .BuildAsync();

        await new TestReportBuilder(Factory.Services)
            .ForMessage(message)
            .InChat(chat)
            .ReportedBy(300002, "reporter")
            .BuildAsync();

        // Act
        await _reportsPage.NavigateAsync();
        await _reportsPage.WaitForLoadAsync();

        // Filter to impersonation only (which doesn't exist)
        await _reportsPage.SelectTypeFilterAsync("Impersonation Alerts");

        // Wait for Blazor to re-render - use web-first assertion
        await Expect(Page.GetByText("No reports found")).ToBeVisibleAsync();

        // Assert - should show empty state
        Assert.That(await _reportsPage.IsEmptyStateVisibleAsync(), Is.True,
            "Should show empty state when no reports match filter");
    }
}
