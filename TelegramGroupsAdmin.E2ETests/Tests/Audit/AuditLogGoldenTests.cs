using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using TelegramGroupsAdmin.Testing.Golden;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Audit;

/// <summary>
/// The Audit Log page's Telegram Moderation Log filters on canonical as it is, as the Owner. The
/// expected row counts are read from this test's clone before the app starts; the page's pager total
/// and the rows of its first page are compared against them.
/// </summary>
[TestFixture]
public class AuditLogGoldenTests : GoldenE2ETestBase
{
    private const long AnchorUserId = GoldenDatasetConstants.ModerationLog.MixedIssuerUserId;

    /// <summary>The moderation table's page size: the first of its pager's PageSizeOptions (25, 50, 100).</summary>
    private const int FirstPageSize = 25;

    /// <summary>What the Issued By column renders for <see cref="SystemActorIds.ExamFlow"/> (Actor.FromSystem).</summary>
    private const string ExamFlowDisplayName = "Exam Flow";

    private AuditLogPage _auditLog = null!;
    private int _totalActionCount;
    private int _anchorUserActionCount;
    private int _examFlowActionCount;

    protected override async Task ArrangeDataAsync(AppDbContext context)
    {
        _totalActionCount = await context.UserActions.CountAsync();

        // Guard the user anchor's shape: a known Telegram user whose actions all fit on the first page,
        // issued by more than one actor kind so the filter is proven on the user column alone.
        var anchorActions = await context.UserActions.AsNoTracking()
            .Where(a => a.UserId == AnchorUserId)
            .Select(a => new { a.WebUserId, a.TelegramUserId, a.SystemIdentifier })
            .ToListAsync();
        _anchorUserActionCount = anchorActions.Count;
        Assert.That(_anchorUserActionCount, Is.InRange(2, FirstPageSize),
            "the user anchor's actions must all fit on the moderation table's first page");
        Assert.That(_anchorUserActionCount, Is.LessThan(_totalActionCount), "the user filter must narrow the table");
        Assert.That(anchorActions.Count(a => a.SystemIdentifier != null), Is.GreaterThan(0), "a system-issued action");
        Assert.That(anchorActions.Count(a => a.WebUserId != null), Is.GreaterThan(0), "a web-user-issued action");
        Assert.That(anchorActions.Count(a => a.TelegramUserId != null), Is.GreaterThan(0), "a Telegram-admin-issued action");
        Assert.That(await context.TelegramUsers.AnyAsync(u => u.TelegramUserId == AnchorUserId),
            "the user anchor must have a telegram_users row so the cell renders the ID caption");

        // The issued-by filter searches what the column renders: a system actor's display name, a web
        // user's email, a Telegram admin's name. Expected sets come from the raw actor columns.
        _examFlowActionCount = await context.UserActions.CountAsync(a => a.SystemIdentifier == SystemActorIds.ExamFlow);
        Assert.That(_examFlowActionCount, Is.InRange(2, FirstPageSize),
            "the exam-flow actions must all fit on the moderation table's first page");
        Assert.That(_examFlowActionCount, Is.LessThan(_totalActionCount), "the issued-by filter must narrow the table");
    }

    [SetUp]
    public async Task OpenModerationLogAsOwner()
    {
        _auditLog = new AuditLogPage(Page);
        await LoginAsOwnerAsync();
        await _auditLog.NavigateToTabAsync("telegram");
        await Expect(_auditLog.ModerationLogTab).ToHaveAttributeAsync("aria-selected", "true");

        // The unfiltered table: every canonical action, first page full.
        await _auditLog.ExpectTotalRowCountAsync(_totalActionCount);
        await Expect(_auditLog.TableRows).ToHaveCountAsync(FirstPageSize);
    }

    [Test]
    public async Task TelegramUserIdFilter_ShowsOnlyThatUsersActions()
    {
        await _auditLog.FilterByTelegramUserIdAsync(AnchorUserId.ToString());

        await _auditLog.ExpectTotalRowCountAsync(_anchorUserActionCount);
        await Expect(_auditLog.TableRows).ToHaveCountAsync(_anchorUserActionCount);
        await Expect(_auditLog.ModerationEntriesForTelegramUser(AnchorUserId)).ToHaveCountAsync(_anchorUserActionCount);
        await Expect(_auditLog.ModerationTelegramUserCells).ToHaveCountAsync(_anchorUserActionCount);
    }

    [Test]
    public async Task IssuedByFilter_ShowsOnlyActionsOfThatSystemActor()
    {
        // Searched the way the column shows it, not by the stored identifier.
        await _auditLog.FilterByIssuedByAsync(ExamFlowDisplayName);

        await _auditLog.ExpectTotalRowCountAsync(_examFlowActionCount);
        await Expect(_auditLog.TableRows).ToHaveCountAsync(_examFlowActionCount);
        await Expect(_auditLog.ModerationEntryWithIssuedBy(ExamFlowDisplayName)).ToHaveCountAsync(_examFlowActionCount);
        await Expect(_auditLog.ModerationIssuedByCells).ToHaveCountAsync(_examFlowActionCount);
    }
}
