using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using TelegramGroupsAdmin.Testing.Golden;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Users;

/// <summary>
/// The Users page on canonical as it is, as the Owner: the trust toggle (the one app write, and the
/// assertion subject), the Trusted / Chat admin badges, View Details, the tab count badges and the
/// Warnings cell for a member whose only warning has expired. Expected counts are read from this
/// test's clone before the app starts, with the tab predicates mirrored here rather than taken
/// from the repository.
/// </summary>
[TestFixture]
public class UsersGoldenTests : GoldenE2ETestBase
{
    private const long UntrustedMemberId = GoldenDatasetConstants.UsersPage.UntrustedActiveMemberId;
    private const long ChatAdminMemberId = GoldenDatasetConstants.UsersPage.ChatAdminMemberId;
    private const long ExpiredWarningMemberId = GoldenDatasetConstants.UsersPage.ExpiredWarningTrustedMemberId;
    private const string OwnerId = GoldenDatasetConstants.WebUsers.OwnerId;

    private UsersPage _users = null!;
    private int _trustActionsBefore;
    private string _expiredWarningMemberDisplayName = string.Empty;
    private int _expiredWarningMemberActiveWarnings;
    private int _allCount;
    private int _activeCount;
    private int _taggedCount;
    private int _trustedCount;
    private int _bannedCount;
    private int _kickedCount;

    protected override async Task ArrangeDataAsync(AppDbContext context)
    {
        var now = DateTimeOffset.UtcNow;

        // The trust subject: untrusted, a plain active member, so the toggle reads "Trust user" on All.
        var subject = await context.TelegramUsers.AsNoTracking()
            .Where(u => u.TelegramUserId == UntrustedMemberId)
            .Select(u => new { u.IsTrusted, u.IsActive, u.IsBanned, u.IsBot })
            .SingleAsync();
        Assert.That(subject.IsTrusted, Is.False, "the trust subject must start untrusted");
        Assert.That(subject.IsActive && !subject.IsBanned && !subject.IsBot, "the trust subject must be a plain active member");
        _trustActionsBefore = await context.UserActions
            .CountAsync(a => a.UserId == UntrustedMemberId && a.ActionType == (int)UserActionType.Trust);

        // The badge anchors: an active admin (also trusted — every non-bot canonical admin is) and a
        // trusted member with no admin seat, so each badge is proven present and absent.
        Assert.That(await context.ChatAdmins.AnyAsync(a => a.TelegramId == ChatAdminMemberId && a.IsActive),
            "the admin anchor must hold an active chat_admins row");
        Assert.That(await context.TelegramUsers.AnyAsync(u => u.TelegramUserId == ChatAdminMemberId && u.IsTrusted && u.IsActive && !u.IsBanned),
            "the admin anchor must be a trusted active member");

        var expiredWarned = await context.TelegramUsers.AsNoTracking()
            .Where(u => u.TelegramUserId == ExpiredWarningMemberId)
            .Select(u => new { u.IsTrusted, u.IsActive, u.IsBanned, u.FirstName, u.LastName, u.Username, u.Warnings })
            .SingleAsync();
        Assert.That(expiredWarned.IsTrusted && expiredWarned.IsActive && !expiredWarned.IsBanned,
            "the expired-warning anchor must be a trusted active member");
        Assert.That(await context.ChatAdmins.AnyAsync(a => a.TelegramId == ExpiredWarningMemberId && a.IsActive), Is.False,
            "the expired-warning anchor must hold no active admin seat");
        Assert.That(expiredWarned.Warnings, Is.Not.Null.And.Not.Empty, "the expired-warning anchor must carry a warning");
        _expiredWarningMemberActiveWarnings = expiredWarned.Warnings!.Count(w => w.ExpiresAt == null || w.ExpiresAt > now);
        Assert.That(_expiredWarningMemberActiveWarnings, Is.Zero, "every warning of the anchor must have expired");
        _expiredWarningMemberDisplayName = TelegramDisplayName.Format(
            expiredWarned.FirstName, expiredWarned.LastName, expiredWarned.Username, ExpiredWarningMemberId);

        // Tab counts under global scope (the Owner), mirrored from the tab definitions: the system user
        // (id 0) is never listed; Tagged is active with a note, a tag row or any warning (expired or not);
        // Banned drops bans whose expiry has passed; Kicked is inactive and not banned.
        var members = context.TelegramUsers.AsNoTracking().Where(u => u.TelegramUserId != 0);
        _allCount = await members.CountAsync();
        _activeCount = await members.CountAsync(u => u.IsActive && !u.IsBanned);
        _trustedCount = await members.CountAsync(u => u.IsTrusted);
        _bannedCount = await members.CountAsync(u => u.IsBanned && (u.BanExpiresAt == null || u.BanExpiresAt > now));
        _kickedCount = await members.CountAsync(u => !u.IsActive && !u.IsBanned);

        var notedIds = await context.AdminNotes.Select(n => n.TelegramUserId).Distinct().ToHashSetAsync();
        var taggedIds = await context.UserTags.Select(t => t.TelegramUserId).Distinct().ToHashSetAsync();
        var activeMembers = await members.Where(u => u.IsActive)
            .Select(u => new { u.TelegramUserId, HasWarnings = u.Warnings != null && u.Warnings.Count > 0 })
            .ToListAsync();
        _taggedCount = activeMembers.Count(u => notedIds.Contains(u.TelegramUserId) || taggedIds.Contains(u.TelegramUserId) || u.HasWarnings);

        Assert.That(_allCount, Is.GreaterThan(_activeCount), "All must list more than Active");
        Assert.That(_taggedCount, Is.GreaterThan(0), "Tagged must be non-empty");
        Assert.That(_kickedCount, Is.GreaterThan(0), "Kicked must be non-empty");
        Assert.That(_bannedCount, Is.GreaterThan(0), "Banned must be non-empty");
    }

    [SetUp]
    public async Task OpenUsersPageAsOwner()
    {
        _users = new UsersPage(Page);
        await LoginAsOwnerAsync();
        await _users.NavigateAsync();
        await _users.WaitForLoadAsync();
    }

    /// <summary>Narrows every tab to the one user and waits for the All table to reload on it.</summary>
    private async Task<ILocator> SearchForAsync(long telegramUserId)
    {
        await _users.SearchUsersAsync(telegramUserId.ToString());
        await _users.ExpectTotalUserCountAsync(1);
        var row = _users.UserRowById(telegramUserId);
        await Expect(row).ToBeVisibleAsync();
        return row;
    }

    [Test]
    public async Task TrustToggle_TrustsTheMemberShowsTheBadgeAndRecordsTheAction()
    {
        var row = await SearchForAsync(UntrustedMemberId);

        await Expect(_users.TrustToggle(row)).ToBeVisibleAsync();
        await Expect(_users.TrustedBadge(row)).ToHaveCountAsync(0);
        await Expect(_users.TabBadgeOf("Trusted")).ToHaveTextAsync("0");

        await _users.TrustToggle(row).ClickAsync();

        // The toggle flips once the page has reloaded its tables after the write.
        await Expect(_users.UntrustToggle(row)).ToBeVisibleAsync();
        await Expect(_users.TrustedBadge(row)).ToBeVisibleAsync();
        await Expect(_users.TrustToggle(row)).ToHaveCountAsync(0);
        await Expect(_users.TabBadgeOf("Trusted")).ToHaveTextAsync("1");
        await Expect(_users.SnackbarWithText("Successfully trusted")).ToBeVisibleAsync();

        await using var ctx = CreateDbContext();
        var isTrusted = await ctx.TelegramUsers.AsNoTracking()
            .Where(u => u.TelegramUserId == UntrustedMemberId).Select(u => u.IsTrusted).SingleAsync();
        Assert.That(isTrusted, Is.True, "telegram_users.is_trusted must be set");

        var trustActions = await ctx.UserActions.AsNoTracking()
            .Where(a => a.UserId == UntrustedMemberId && a.ActionType == (int)UserActionType.Trust)
            .Select(a => new { a.WebUserId, a.ExpiresAt })
            .ToListAsync();
        Assert.That(trustActions, Has.Count.EqualTo(_trustActionsBefore + 1), "exactly one Trust action is recorded");
        var recorded = trustActions.Single(a => a.WebUserId == OwnerId);
        Assert.That(recorded.ExpiresAt, Is.Null, "a manual trust does not expire");
    }

    [Test]
    public async Task Badges_RenderTrustedAndChatAdminForTheMembersThatHoldThem()
    {
        var admin = await SearchForAsync(ChatAdminMemberId);
        await Expect(_users.TrustedBadge(admin)).ToBeVisibleAsync();
        await Expect(_users.AdminBadge(admin)).ToBeVisibleAsync();

        var trustedOnly = await SearchForAsync(ExpiredWarningMemberId);
        await Expect(_users.TrustedBadge(trustedOnly)).ToBeVisibleAsync();
        await Expect(_users.AdminBadge(trustedOnly)).ToHaveCountAsync(0);
    }

    [Test]
    public async Task ViewDetails_OpensTheDialogForThatMemberAndCloses()
    {
        var row = await SearchForAsync(ExpiredWarningMemberId);

        await _users.ClickViewDetailsAsync(row);

        await Expect(_users.Dialog).ToContainTextAsync("User Details");
        // The dialog's own title is also an h6, so the profile header is read from the content area.
        await Expect(_users.Dialog.Locator(".mud-dialog-content .mud-typography-h6").First).ToHaveTextAsync(_expiredWarningMemberDisplayName);
        await Expect(_users.Dialog).ToContainTextAsync(new Regex($@"ID: {ExpiredWarningMemberId}(?!\d)"));

        await _users.CloseDialogAsync();
        await Expect(_users.Dialog).ToHaveCountAsync(0);
    }

    /// <summary>What MudBadge renders for a count: its default Max of 99 caps larger counts at "99+".</summary>
    private static string BadgeText(int count) => count > 99 ? "99+" : count.ToString();

    [Test]
    public async Task TabBadges_MatchTheCountsEachTabLists()
    {
        await Expect(_users.TabBadgeOf("All")).ToHaveTextAsync(BadgeText(_allCount));
        await Expect(_users.TabBadgeOf("Active")).ToHaveTextAsync(BadgeText(_activeCount));
        await Expect(_users.TabBadgeOf("Tagged")).ToHaveTextAsync(BadgeText(_taggedCount));
        await Expect(_users.TabBadgeOf("Trusted")).ToHaveTextAsync(BadgeText(_trustedCount));
        await Expect(_users.TabBadgeOf("Banned")).ToHaveTextAsync(BadgeText(_bannedCount));
        await Expect(_users.TabBadgeOf("Kicked")).ToHaveTextAsync(BadgeText(_kickedCount));

        // The badge caps at 99+, so each tab's exact count is proven through its pager total.
        await _users.ExpectTotalUserCountAsync(_allCount);
        await _users.SelectTabAsync("Active");
        await _users.ExpectTotalUserCountAsync(_activeCount);
        await _users.SelectTabAsync("Tagged");
        await _users.ExpectTotalUserCountAsync(_taggedCount);
        await _users.SelectTabAsync("Trusted");
        await _users.ExpectTotalUserCountAsync(_trustedCount);
        await _users.SelectTabAsync("Banned");
        await _users.ExpectTotalUserCountAsync(_bannedCount);
        await _users.SelectTabAsync("Kicked");
        await _users.ExpectTotalUserCountAsync(_kickedCount);
    }

    [Test]
    public async Task WarningsCell_ShowsNoneOnActiveWhenTheOnlyWarningHasExpired()
    {
        await SearchForAsync(ExpiredWarningMemberId);
        await _users.SelectTabAsync("Active");

        var row = _users.UserRowById(ExpiredWarningMemberId);
        await Expect(row).ToBeVisibleAsync();
        Assert.That(_expiredWarningMemberActiveWarnings, Is.Zero);
        await Expect(_users.WarningsCellOf(row)).ToHaveTextAsync("None");
    }
}
