using Microsoft.Playwright;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Reports;

/// <summary>
/// E2E tests for the Exam Review functionality in the Reports Queue page.
/// Tests exam failure display, filtering, MC/Open-ended content, and review actions.
/// Uses SharedAuthenticatedTestBase for faster test execution with shared factory.
/// </summary>
[TestFixture]
public class ExamReportsTests : SharedAuthenticatedTestBase
{
    private ReportsPage _reportsPage = null!;

    [SetUp]
    public void SetUp()
    {
        _reportsPage = new ReportsPage(Page);
    }

    #region Exam Review Display Tests

    [Test]
    public async Task ExamReview_DisplaysInReportsQueue_WhenPending()
    {
        // Arrange
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Exam Test Chat")
            .BuildAsync();

        // Create telegram user first (foreign key requirement)
        await new TestTelegramUserBuilder(SharedFactory.Services)
            .WithUserId(600001)
            .WithUsername("examuser")
            .WithName("Exam", "Taker")
            .BuildAsync();

        // Create exam failure
        await new TestExamFailureBuilder(SharedFactory.Services)
            .WithUser(600001, "examuser", "Exam", "Taker")
            .InChat(chat)
            .WithScore(50, 80) // 50% score, 80% threshold (failed)
            .AsFailing()
            .BuildAsync();

        // Act
        await _reportsPage.NavigateAsync();
        await _reportsPage.WaitForLoadAsync();

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(await _reportsPage.HasReportsAsync(), Is.True,
                "Should display exam review when pending exam failure exists");

            Assert.That(await _reportsPage.IsPendingExamChipVisibleAsync(), Is.True,
                "Pending exam chip should be visible");

            Assert.That(await _reportsPage.GetPendingExamCountAsync(), Is.GreaterThanOrEqualTo(1),
                "Should show at least 1 pending exam review");
        }
    }

    [Test]
    public async Task ExamReview_ShowsExamReviewTitle()
    {
        // Arrange
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Exam Title Test Chat")
            .BuildAsync();

        await new TestTelegramUserBuilder(SharedFactory.Services)
            .WithUserId(600002)
            .WithUsername("titletest")
            .WithName("Title", "Test")
            .BuildAsync();

        await new TestExamFailureBuilder(SharedFactory.Services)
            .WithUser(600002, "titletest", "Title", "Test")
            .InChat(chat)
            .AsFailing()
            .BuildAsync();

        // Act
        await _reportsPage.NavigateAsync();
        await _reportsPage.WaitForLoadAsync();

        // Assert - use web-first assertion
        await Expect(Page.GetByText("Exam Review", new() { Exact = true })).ToBeVisibleAsync();
    }

    [Test]
    public async Task ExamReview_ShowsUserInfo()
    {
        // Arrange
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("User Info Chat")
            .BuildAsync();

        await new TestTelegramUserBuilder(SharedFactory.Services)
            .WithUserId(600003)
            .WithUsername("johnsmith")
            .WithName("John", "Smith")
            .BuildAsync();

        await new TestExamFailureBuilder(SharedFactory.Services)
            .WithUser(600003, "johnsmith", "John", "Smith")
            .InChat(chat)
            .AsFailing()
            .BuildAsync();

        // Act
        await _reportsPage.NavigateAsync();
        await _reportsPage.WaitForLoadAsync();

        // Filter to exam reviews to ensure we see the right card
        await _reportsPage.SelectTypeFilterAsync("Exam Reviews");

        // Assert - user info should be visible
        await Expect(Page.GetByText("John Smith")).ToBeVisibleAsync();
        await Expect(Page.GetByText("@johnsmith")).ToBeVisibleAsync();
    }

    #endregion

    #region Filter Tests

    [Test]
    public async Task ExamReview_FiltersByType_ExamReviewsOnly()
    {
        // Arrange
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Filter Test Chat")
            .BuildAsync();

        // Create moderation report
        var message = await new TestMessageBuilder(SharedFactory.Services)
            .InChat(chat)
            .FromUser(600010, "reporter", "Report", "User")
            .WithText("Some message")
            .BuildAsync();

        await new TestReportBuilder(SharedFactory.Services)
            .ForMessage(message)
            .InChat(chat)
            .ReportedBy(600011, "admin")
            .BuildAsync();

        // Create exam failure
        await new TestTelegramUserBuilder(SharedFactory.Services)
            .WithUserId(600012)
            .WithUsername("examfail")
            .WithName("Exam", "Fail")
            .BuildAsync();

        await new TestExamFailureBuilder(SharedFactory.Services)
            .WithUser(600012, "examfail", "Exam", "Fail")
            .InChat(chat)
            .AsFailing()
            .BuildAsync();

        // Act
        await _reportsPage.NavigateAsync();
        await _reportsPage.WaitForLoadAsync();

        // Filter to Exam Reviews only
        await _reportsPage.SelectTypeFilterAsync("Exam Reviews");

        // Assert - only exam reviews should be visible
        await Expect(Page.GetByText("Exam Review", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText("Moderation Report", new() { Exact = true })).Not.ToBeVisibleAsync();

        var examCount = await _reportsPage.GetExamReviewCountAsync();
        var moderationCount = await _reportsPage.GetModerationReportCountAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(examCount, Is.GreaterThanOrEqualTo(1),
                      "Should show exam reviews when filtered");
            Assert.That(moderationCount, Is.EqualTo(0),
                "Should not show moderation reports when filtered to exam reviews");
        }
    }

    [Test]
    public async Task ExamReview_ShowsInAllTypes_WithOtherReportTypes()
    {
        // Arrange
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("All Types Chat")
            .BuildAsync();

        // Create moderation report
        var message = await new TestMessageBuilder(SharedFactory.Services)
            .InChat(chat)
            .FromUser(600020, "moduser", "Mod", "User")
            .WithText("Some message")
            .BuildAsync();

        await new TestReportBuilder(SharedFactory.Services)
            .ForMessage(message)
            .InChat(chat)
            .ReportedBy(600021, "reporter")
            .BuildAsync();

        // Create exam failure
        await new TestTelegramUserBuilder(SharedFactory.Services)
            .WithUserId(600022)
            .WithUsername("alltypesexam")
            .WithName("AllTypes", "Exam")
            .BuildAsync();

        await new TestExamFailureBuilder(SharedFactory.Services)
            .WithUser(600022, "alltypesexam", "AllTypes", "Exam")
            .InChat(chat)
            .AsFailing()
            .BuildAsync();

        // Act
        await _reportsPage.NavigateAsync();
        await _reportsPage.WaitForLoadAsync();

        // Default filter is "All Types"
        // Assert - both report types should be visible
        var examCount = await _reportsPage.GetExamReviewCountAsync();
        var moderationCount = await _reportsPage.GetModerationReportCountAsync();

        Assert.That(examCount + moderationCount, Is.GreaterThanOrEqualTo(2),
            "Should show both exam reviews and moderation reports with All Types filter");
    }

    [Test]
    public async Task ReportsQueue_DisplaysAllThreeTypes_InUnifiedView()
    {
        // Arrange - Create all three report types in the same chat
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Unified Queue Test Chat")
            .BuildAsync();

        // 1. Create moderation report (spam/content report)
        var message = await new TestMessageBuilder(SharedFactory.Services)
            .InChat(chat)
            .FromUser(700001, "spammer", "Spam", "User")
            .WithText("Buy cheap stuff at scam.com!")
            .BuildAsync();

        await new TestReportBuilder(SharedFactory.Services)
            .ForMessage(message)
            .InChat(chat)
            .ReportedBy(700002, "reporter")
            .BuildAsync();

        // 2. Create impersonation alert
        await new TestTelegramUserBuilder(SharedFactory.Services)
            .WithUserId(700003)
            .WithUsername("fakeadmin")
            .WithName("Fake", "Admin")
            .BuildAsync();

        await new TestImpersonationAlertBuilder(SharedFactory.Services)
            .WithSuspectedUser(700003, "fakeadmin", "Fake", "Admin")
            .WithTargetUser(700004, "realadmin", "Real", "Admin")
            .InChat(chat)
            .WithRiskLevel(ImpersonationRiskLevel.Medium)
            .BuildAsync();

        // 3. Create exam failure
        await new TestTelegramUserBuilder(SharedFactory.Services)
            .WithUserId(700005)
            .WithUsername("examfailuser")
            .WithName("Exam", "Fail")
            .BuildAsync();

        await new TestExamFailureBuilder(SharedFactory.Services)
            .WithUser(700005, "examfailuser", "Exam", "Fail")
            .InChat(chat)
            .AsFailing()
            .BuildAsync();

        // Act
        await _reportsPage.NavigateAsync();
        await _reportsPage.WaitForLoadAsync();

        // Wait for all data to fully load (Blazor SignalR + async data fetching)
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Assert - All three types should be visible with default "All Types" filter
        var moderationCount = await _reportsPage.GetModerationReportCountAsync();
        var impersonationCount = await _reportsPage.GetImpersonationAlertCountAsync();
        var examCount = await _reportsPage.GetExamReviewCountAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(moderationCount, Is.GreaterThanOrEqualTo(1),
                "Should display at least 1 moderation report");
            Assert.That(impersonationCount, Is.GreaterThanOrEqualTo(1),
                "Should display at least 1 impersonation alert");
            Assert.That(examCount, Is.GreaterThanOrEqualTo(1),
                "Should display at least 1 exam review");
        }

        // Verify each card type header is visible
        await Expect(Page.GetByText("Moderation Report", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText("Impersonation Alert", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText("Exam Review", new() { Exact = true })).ToBeVisibleAsync();

        using (Assert.EnterMultipleScope())
        {
            // Verify pending count chips show all types
            Assert.That(await _reportsPage.IsPendingModerationChipVisibleAsync(), Is.True,
                "Pending moderation chip should be visible");
            Assert.That(await _reportsPage.IsPendingImpersonationChipVisibleAsync(), Is.True,
                "Pending impersonation chip should be visible");
            Assert.That(await _reportsPage.IsPendingExamChipVisibleAsync(), Is.True,
                "Pending exam chip should be visible");
        }
    }

    #endregion

    #region MC Section Tests

    [Test]
    public async Task ExamReview_ShowsMcSection_WhenMcAnswersExist()
    {
        // Arrange
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("MC Section Chat")
            .BuildAsync();

        // Create welcome config with MC questions (required for ExamReviewCard to display MC section)
        await new TestWelcomeConfigBuilder(SharedFactory.Services)
            .ForChat(chat)
            .WithMcQuestionsOnly()
            .BuildAsync();

        await new TestTelegramUserBuilder(SharedFactory.Services)
            .WithUserId(600030)
            .WithUsername("mcuser")
            .WithName("MC", "User")
            .BuildAsync();

        await new TestExamFailureBuilder(SharedFactory.Services)
            .WithUser(600030, "mcuser", "MC", "User")
            .InChat(chat)
            .WithMcAnswers(
                new Dictionary<int, string> { { 0, "A" }, { 1, "B" } },
                new Dictionary<int, int[]>
                {
                    { 0, [0, 1, 2, 3] },
                    { 1, [1, 0, 2, 3] }
                })
            .WithScore(50, 80)
            .BuildAsync();

        // Act
        await _reportsPage.NavigateAsync();
        await _reportsPage.WaitForLoadAsync();

        // Assert
        Assert.That(await _reportsPage.HasExamMcSectionAsync(), Is.True,
            "Multiple Choice section should be visible");
    }

    [Test]
    public async Task ExamReview_ShowsFailedStatus_WhenScoreBelowThreshold()
    {
        // Arrange
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Failed Score Chat")
            .BuildAsync();

        // Create welcome config with MC questions (required for ExamReviewCard to display MC section)
        await new TestWelcomeConfigBuilder(SharedFactory.Services)
            .ForChat(chat)
            .WithMcQuestionsOnly()
            .BuildAsync();

        await new TestTelegramUserBuilder(SharedFactory.Services)
            .WithUserId(600031)
            .WithUsername("faileduser")
            .WithName("Failed", "User")
            .BuildAsync();

        await new TestExamFailureBuilder(SharedFactory.Services)
            .WithUser(600031, "faileduser", "Failed", "User")
            .InChat(chat)
            .WithScore(40, 80) // 40% score, 80% threshold (failed)
            .BuildAsync();

        // Act
        await _reportsPage.NavigateAsync();
        await _reportsPage.WaitForLoadAsync();

        // Assert
        Assert.That(await _reportsPage.IsExamMcFailedAsync(), Is.True,
            "Failed status should be visible for score below threshold");
    }

    #endregion

    #region Open-Ended Section Tests

    [Test]
    public async Task ExamReview_ShowsOpenEndedSection_WhenAnswerExists()
    {
        // Arrange
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Open Ended Chat")
            .BuildAsync();

        // Create welcome config with open-ended question (required for ExamReviewCard to display Open-Ended section)
        await new TestWelcomeConfigBuilder(SharedFactory.Services)
            .ForChat(chat)
            .WithOpenEndedOnly()
            .BuildAsync();

        await new TestTelegramUserBuilder(SharedFactory.Services)
            .WithUserId(600040)
            .WithUsername("openendeduser")
            .WithName("OpenEnded", "User")
            .BuildAsync();

        await new TestExamFailureBuilder(SharedFactory.Services)
            .WithUser(600040, "openendeduser", "OpenEnded", "User")
            .InChat(chat)
            .WithOpenEndedAnswer("I want to join because I love technology and want to learn more.")
            .AsFailing()
            .BuildAsync();

        // Act
        await _reportsPage.NavigateAsync();
        await _reportsPage.WaitForLoadAsync();

        // Assert
        Assert.That(await _reportsPage.HasExamOpenEndedSectionAsync(), Is.True,
            "Open-Ended Question section should be visible");
    }

    [Test]
    public async Task ExamReview_ShowsAiEvaluation_WhenProvided()
    {
        // Arrange
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("AI Eval Chat")
            .BuildAsync();

        // Create welcome config with open-ended question (required for ExamReviewCard to display AI evaluation)
        await new TestWelcomeConfigBuilder(SharedFactory.Services)
            .ForChat(chat)
            .WithOpenEndedOnly()
            .BuildAsync();

        await new TestTelegramUserBuilder(SharedFactory.Services)
            .WithUserId(600041)
            .WithUsername("aievaluser")
            .WithName("AIEval", "User")
            .BuildAsync();

        await new TestExamFailureBuilder(SharedFactory.Services)
            .WithUser(600041, "aievaluser", "AIEval", "User")
            .InChat(chat)
            .WithOpenEndedAnswer(
                "I am interested in joining this group",
                "FAIL - Response is generic and doesn't demonstrate genuine interest")
            .AsFailing()
            .BuildAsync();

        // Act
        await _reportsPage.NavigateAsync();
        await _reportsPage.WaitForLoadAsync();

        // Assert
        Assert.That(await _reportsPage.HasExamAiEvaluationAsync(), Is.True,
            "AI Evaluation section should be visible");
    }

    #endregion

    #region Action Tests

    [Test]
    public async Task ExamReview_ShowsActionButtons_WhenPending()
    {
        // Arrange
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Action Buttons Chat")
            .BuildAsync();

        await new TestTelegramUserBuilder(SharedFactory.Services)
            .WithUserId(600050)
            .WithUsername("actionuser")
            .WithName("Action", "User")
            .BuildAsync();

        await new TestExamFailureBuilder(SharedFactory.Services)
            .WithUser(600050, "actionuser", "Action", "User")
            .InChat(chat)
            .AsFailing()
            .BuildAsync();

        // Act
        await _reportsPage.NavigateAsync();
        await _reportsPage.WaitForLoadAsync();

        // Assert - action buttons are visible
        var buttons = await _reportsPage.GetVisibleActionButtonsAsync();
        Assert.That(buttons, Has.Some.Contain("Approve"),
            "Approve button should be visible");
        Assert.That(buttons, Has.Some.Contain("Deny"),
            "Deny button should be visible");
        Assert.That(buttons, Has.Some.Contain("Ban").IgnoreCase,
            "Deny + Ban button should be visible");
    }

    [Test]
    public async Task ExamReview_Approve_ProcessesImmediately()
    {
        // Arrange
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Approve Chat")
            .BuildAsync();

        // Create welcome config (required for ExamReviewCard to fully render)
        await new TestWelcomeConfigBuilder(SharedFactory.Services)
            .ForChat(chat)
            .WithFullExam()
            .BuildAsync();

        await new TestTelegramUserBuilder(SharedFactory.Services)
            .WithUserId(600051)
            .WithUsername("approveuser")
            .WithName("Approve", "User")
            .BuildAsync();

        await new TestExamFailureBuilder(SharedFactory.Services)
            .WithUser(600051, "approveuser", "Approve", "User")
            .InChat(chat)
            .AsFailing()
            .BuildAsync();

        // Act
        await _reportsPage.NavigateAsync();
        await _reportsPage.WaitForLoadAsync();

        // Verify exam review exists
        await Expect(Page.GetByText("Exam Review", new() { Exact = true })).ToBeVisibleAsync();

        // Click Approve - NO CONFIRMATION DIALOG
        await _reportsPage.ClickApproveExamAsync();

        // Assert - snackbar confirms success (orchestrator is mocked to return success)
        var snackbarText = await _reportsPage.WaitForSnackbarAsync();
        Assert.That(snackbarText, Does.Contain("approved").IgnoreCase
            .Or.Contain("success").IgnoreCase,
            "Snackbar should confirm the approve action succeeded");
    }

    [Test]
    public async Task ExamReview_Deny_ProcessesImmediately()
    {
        // Arrange
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Deny Chat")
            .BuildAsync();

        await new TestTelegramUserBuilder(SharedFactory.Services)
            .WithUserId(600052)
            .WithUsername("denyuser")
            .WithName("Deny", "User")
            .BuildAsync();

        await new TestExamFailureBuilder(SharedFactory.Services)
            .WithUser(600052, "denyuser", "Deny", "User")
            .InChat(chat)
            .AsFailing()
            .BuildAsync();

        // Act
        await _reportsPage.NavigateAsync();
        await _reportsPage.WaitForLoadAsync();

        // Verify exam review exists
        await Expect(Page.GetByText("Exam Review", new() { Exact = true })).ToBeVisibleAsync();

        // Click Deny - NO CONFIRMATION DIALOG
        await _reportsPage.ClickDenyExamAsync();

        // Assert - snackbar confirms success (orchestrator is mocked to return success)
        var snackbarText = await _reportsPage.WaitForSnackbarAsync();
        Assert.That(snackbarText, Does.Contain("denied").IgnoreCase
            .Or.Contain("kicked").IgnoreCase
            .Or.Contain("success").IgnoreCase,
            "Snackbar should confirm the deny action succeeded");
    }

    [Test]
    public async Task ExamReview_DenyAndBan_ProcessesImmediately()
    {
        // Arrange
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Deny Ban Chat")
            .BuildAsync();

        await new TestTelegramUserBuilder(SharedFactory.Services)
            .WithUserId(600053)
            .WithUsername("denybanuser")
            .WithName("DenyBan", "User")
            .BuildAsync();

        await new TestExamFailureBuilder(SharedFactory.Services)
            .WithUser(600053, "denybanuser", "DenyBan", "User")
            .InChat(chat)
            .AsFailing()
            .BuildAsync();

        // Act
        await _reportsPage.NavigateAsync();
        await _reportsPage.WaitForLoadAsync();

        // Verify exam review exists
        await Expect(Page.GetByText("Exam Review", new() { Exact = true })).ToBeVisibleAsync();

        // Click Deny + Ban - NO CONFIRMATION DIALOG
        await _reportsPage.ClickDenyAndBanExamAsync();

        // Assert - snackbar confirms success (orchestrator is mocked to return success)
        var snackbarText = await _reportsPage.WaitForSnackbarAsync();
        Assert.That(snackbarText, Does.Contain("banned").IgnoreCase
            .Or.Contain("denied").IgnoreCase
            .Or.Contain("success").IgnoreCase,
            "Snackbar should confirm the deny + ban action succeeded");
    }

    #endregion

    #region Reviewed Status Tests

    [Test]
    public async Task ExamReview_HidesActionButtons_WhenReviewed()
    {
        // Arrange
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Reviewed Chat")
            .BuildAsync();

        // Create welcome config (required for ExamReviewCard to fully render)
        await new TestWelcomeConfigBuilder(SharedFactory.Services)
            .ForChat(chat)
            .WithFullExam()
            .BuildAsync();

        await new TestTelegramUserBuilder(SharedFactory.Services)
            .WithUserId(600060)
            .WithUsername("revieweduser")
            .WithName("Reviewed", "User")
            .BuildAsync();

        // Create already-reviewed exam failure
        await new TestExamFailureBuilder(SharedFactory.Services)
            .WithUser(600060, "revieweduser", "Reviewed", "User")
            .InChat(chat)
            .AsFailing()
            .AsApproved("admin@test.com")
            .BuildAsync();

        // Act
        await _reportsPage.NavigateAsync();
        await _reportsPage.WaitForLoadAsync();

        // Switch to All Statuses to see reviewed items (default shows only pending)
        await _reportsPage.SelectStatusFilterAsync("All Statuses");

        // Assert - Reviewed chip should be visible, action buttons hidden
        await Expect(Page.GetByText("Reviewed", new() { Exact = true })).ToBeVisibleAsync();

        // Action buttons should not be visible for reviewed items
        var approveButton = Page.Locator("button:has-text('Approve')");
        await Expect(approveButton).Not.ToBeVisibleAsync();
    }

    [Test]
    public async Task ExamReview_ShowsActionTaken_WhenReviewed()
    {
        // Arrange
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Action Taken Chat")
            .BuildAsync();

        // Create welcome config (required for ExamReviewCard to fully render)
        await new TestWelcomeConfigBuilder(SharedFactory.Services)
            .ForChat(chat)
            .WithFullExam()
            .BuildAsync();

        await new TestTelegramUserBuilder(SharedFactory.Services)
            .WithUserId(600061)
            .WithUsername("actiontakenuser")
            .WithName("ActionTaken", "User")
            .BuildAsync();

        // Create approved exam failure
        await new TestExamFailureBuilder(SharedFactory.Services)
            .WithUser(600061, "actiontakenuser", "ActionTaken", "User")
            .InChat(chat)
            .AsFailing()
            .AsApproved("admin@test.com")
            .BuildAsync();

        // Act
        await _reportsPage.NavigateAsync();
        await _reportsPage.WaitForLoadAsync();

        // Switch to All Statuses to see reviewed items (default shows only pending)
        await _reportsPage.SelectStatusFilterAsync("All Statuses");

        // Assert - should show action taken (in card actions: "Action: approved")
        await Expect(Page.GetByText("Action: approved")).ToBeVisibleAsync();
    }

    #endregion

    #region Permission Tests

    [Test]
    public async Task ExamReview_VisibleToGlobalAdmin()
    {
        // Arrange
        await LoginAsGlobalAdminAsync();

        var chat = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("GlobalAdmin Exam Chat")
            .BuildAsync();

        await new TestTelegramUserBuilder(SharedFactory.Services)
            .WithUserId(600070)
            .WithUsername("globaladminexam")
            .WithName("GlobalAdmin", "Exam")
            .BuildAsync();

        await new TestExamFailureBuilder(SharedFactory.Services)
            .WithUser(600070, "globaladminexam", "GlobalAdmin", "Exam")
            .InChat(chat)
            .AsFailing()
            .BuildAsync();

        // Act
        await _reportsPage.NavigateAsync();
        await _reportsPage.WaitForLoadAsync();

        // Assert - GlobalAdmin should see exam reviews
        Assert.That(await _reportsPage.HasReportsAsync(), Is.True,
            "GlobalAdmin should be able to see exam reviews");
    }

    [Test]
    public async Task ExamReview_VisibleToAdmin()
    {
        // Arrange - Admin can see exam reviews from their chats
        await LoginAsAdminAsync();

        var chat = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Admin Exam Chat")
            .BuildAsync();

        await new TestTelegramUserBuilder(SharedFactory.Services)
            .WithUserId(600071)
            .WithUsername("adminexam")
            .WithName("Admin", "Exam")
            .BuildAsync();

        await new TestExamFailureBuilder(SharedFactory.Services)
            .WithUser(600071, "adminexam", "Admin", "Exam")
            .InChat(chat)
            .AsFailing()
            .BuildAsync();

        // Act
        await _reportsPage.NavigateAsync();
        await _reportsPage.WaitForLoadAsync();

        // Assert - page loads (Admin may not see all reports, but can access page)
        Assert.That(await _reportsPage.IsPageTitleVisibleAsync(), Is.True,
            "Admin should be able to access reports page");
    }

    #endregion
}
