using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Testing.Golden;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Reports;

/// <summary>
/// The Reports queue's Warn action on canonical data, as the Owner: the canonical pending moderation
/// report (<see cref="GoldenDatasetConstants.Reports.PendingModerationReportId"/>) is the only app write,
/// and its outcome is read back from this test's clone.
/// </summary>
/// <remarks>
/// The E2E factory replaces <c>IBotModerationService</c> (the writer of the warnings and
/// <c>user_actions</c> rows) with a mock, so the warning itself is asserted at that boundary: the
/// <see cref="WarnIntent"/> the app dispatched for the reported user, not a <c>user_actions</c> row.
/// </remarks>
[TestFixture]
public class ReportsGoldenTests : GoldenE2ETestBase
{
    private const long ReportId = GoldenDatasetConstants.Reports.PendingModerationReportId;
    private const long ReportedUserId = GoldenDatasetConstants.Reports.PendingFixturesTelegramUserId;

    private ReportsPage _reports = null!;
    private long _reportChatId;
    private int _pendingModerationCount;
    private int _pendingExamCount;
    private int _visibleModerationCount;

    protected override async Task ArrangeDataAsync(AppDbContext context)
    {
        // Guard the anchor's shape: a pending content report whose message resolves to the reported user.
        var report = await context.Reports.AsNoTracking()
            .Where(r => r.Id == ReportId)
            .Select(r => new { r.Status, r.Type, r.MessageId, r.ChatId, r.ReviewedBy, r.ActionTaken })
            .SingleAsync();
        Assert.That(report.Status, Is.EqualTo((int)ReportStatus.Pending), "canonical report 186 must be pending");
        Assert.That(report.Type, Is.EqualTo((short)ReportType.ContentReport), "canonical report 186 must be a content report");
        Assert.That(report.ReviewedBy, Is.Null);
        Assert.That(report.ActionTaken, Is.Null);
        _reportChatId = report.ChatId;

        var authorId = await context.Messages.AsNoTracking()
            .Where(m => m.MessageId == report.MessageId && m.ChatId == report.ChatId)
            .Select(m => m.UserId)
            .SingleAsync();
        Assert.That(authorId, Is.EqualTo(ReportedUserId), "canonical report 186 must point at a message by the pending-fixtures user");

        // The Owner sees every non-deleted managed chat; the page's pending chips count those reports.
        var visibleChatIds = await context.ManagedChats.AsNoTracking()
            .Where(c => !c.IsDeleted).Select(c => c.ChatId).ToListAsync();
        Assert.That(visibleChatIds, Does.Contain(_reportChatId), "the report's chat must be a managed chat");

        var pending = await context.Reports.AsNoTracking()
            .Where(r => r.Status == (int)ReportStatus.Pending && visibleChatIds.Contains(r.ChatId))
            .Select(r => r.Type)
            .ToListAsync();
        _pendingModerationCount = pending.Count(t => t == (short)ReportType.ContentReport);
        _pendingExamCount = pending.Count(t => t == (short)ReportType.ExamResult);
        Assert.That(_pendingModerationCount, Is.GreaterThan(0));
        Assert.That(_pendingExamCount, Is.GreaterThan(0), "an exam review must stay pending to anchor the post-action render");

        // "All Statuses" lists every moderation report of a visible chat, whatever its status.
        _visibleModerationCount = await context.Reports.AsNoTracking()
            .CountAsync(r => r.Type == (short)ReportType.ContentReport && visibleChatIds.Contains(r.ChatId));
        Assert.That(_visibleModerationCount, Is.GreaterThan(_pendingModerationCount), "resolved moderation reports must exist for the All Statuses view");
    }

    [SetUp]
    public async Task OpenReportsAsOwner()
    {
        _reports = new ReportsPage(Page);
        await LoginAsOwnerAsync();
        await _reports.NavigateAsync();
        await _reports.WaitForLoadAsync();
    }

    [Test]
    public async Task Warn_ResolvesThePendingReportAndWarnsTheReportedUser()
    {
        var card = _reports.ModerationReportCardFor(ReportedUserId);

        await Expect(_reports.PendingModerationChip)
            .ToHaveTextAsync(new Regex($@"^\s*{_pendingModerationCount}\s+Moderation"));
        await Expect(_reports.ModerationReportHeaders).ToHaveCountAsync(_pendingModerationCount);
        await Expect(card).ToHaveCountAsync(1);
        await Expect(card.Locator(".mud-card-header .mud-chip")).ToContainTextAsync("Pending");

        await _reports.ClickWarnAsync();

        await Expect(_reports.Snackbar).ToContainTextAsync(new Regex("Warning issued"));

        // The reload after the action keeps the pending exam review on the page: that render is the
        // one the absence checks below run against.
        await Expect(_reports.ExamReviewHeaders).ToHaveCountAsync(_pendingExamCount);
        await Expect(_reports.ModerationReportHeaders).ToHaveCountAsync(_pendingModerationCount - 1);
        await Expect(card).ToHaveCountAsync(0);
        if (_pendingModerationCount == 1)
        {
            await Expect(_reports.PendingModerationChip).ToHaveCountAsync(0);
        }

        // Under "All Statuses" every moderation report of a visible chat is listed and the same card
        // is back, now Reviewed and without its action buttons.
        await _reports.SelectStatusFilterAsync("All Statuses");
        await Expect(_reports.ModerationReportHeaders).ToHaveCountAsync(_visibleModerationCount);
        await Expect(card).ToHaveCountAsync(1);
        await Expect(card.Locator(".mud-card-header .mud-chip")).ToContainTextAsync("Reviewed");
        await Expect(card.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Warn" })).ToHaveCountAsync(0);

        await using (var context = CreateDbContext())
        {
            var report = await context.Reports.AsNoTracking()
                .Where(r => r.Id == ReportId)
                .Select(r => new { r.Status, r.ActionTaken, r.ReviewedBy, r.ReviewedAt })
                .SingleAsync();
            Assert.That(report.Status, Is.EqualTo((int)ReportStatus.Reviewed));
            Assert.That(report.ActionTaken, Is.EqualTo("warn"));
            Assert.That(report.ReviewedBy, Is.EqualTo(GoldenDatasetConstants.WebUsers.OwnerEmail));
            Assert.That(report.ReviewedAt, Is.Not.Null);
        }

        await Factory.MockBotModeration.Received(1).WarnUserAsync(
            Arg.Is<WarnIntent>(i => i!.User.Id == ReportedUserId
                                    && i.Chat.Id == _reportChatId
                                    && i.OriginReportId == ReportId
                                    && i.Executor.WebUserId == GoldenDatasetConstants.WebUsers.OwnerId),
            Arg.Any<CancellationToken>());
    }
}
