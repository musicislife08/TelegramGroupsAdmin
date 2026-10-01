using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using TelegramGroupsAdmin.Testing.Golden;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Users;

/// <summary>
/// The Users page Warnings cell for a member whose warning is in force. Canonical's warnings all
/// expired with the snapshot (a warning counts only while ExpiresAt is ahead of NOW()), so the
/// anchor's warning is re-timed with <c>ExtendTelegramUserWarnings</c> before the app starts and the
/// expected count is read back with the same active-warning predicate the page applies.
/// </summary>
[TestFixture]
public class UsersActiveWarningGoldenTests : GoldenE2ETestBase
{
    private const long WarnedMemberId = GoldenDatasetConstants.UsersPage.WarnedTrustedMemberId;

    private UsersPage _users = null!;
    private int _activeWarningCount;

    protected override async Task ArrangeDataAsync(AppDbContext context)
    {
        var now = DateTimeOffset.UtcNow;

        var before = await context.TelegramUsers.AsNoTracking()
            .Where(u => u.TelegramUserId == WarnedMemberId)
            .Select(u => new { u.IsActive, u.IsBanned, u.Warnings })
            .SingleAsync();
        Assert.That(before.IsActive && !before.IsBanned, "the warned anchor must be listed on Active");
        Assert.That(before.Warnings, Is.Not.Null.And.Not.Empty, "the warned anchor must carry a warning");
        Assert.That(before.Warnings!.All(w => w.ExpiresAt < now), "canonical's warnings are all expired; the re-time is what puts one in force");

        await GoldenDataset.Mutate(context)
            .ExtendTelegramUserWarnings(WarnedMemberId, TimeSpan.FromDays(30))
            .ApplyAsync();

        var after = await context.TelegramUsers.AsNoTracking()
            .Where(u => u.TelegramUserId == WarnedMemberId).Select(u => u.Warnings).SingleAsync();
        _activeWarningCount = after!.Count(w => w.ExpiresAt == null || w.ExpiresAt > now);
        Assert.That(_activeWarningCount, Is.EqualTo(before.Warnings.Count), "every warning of the anchor is now in force");
    }

    [SetUp]
    public async Task OpenUsersPageAsOwner()
    {
        _users = new UsersPage(Page);
        await LoginAsOwnerAsync();
        await _users.NavigateAsync();
        await _users.WaitForLoadAsync();
        await _users.SearchUsersAsync(WarnedMemberId.ToString());
        await _users.ExpectTotalUserCountAsync(1);
    }

    [Test]
    public async Task WarningsCell_ShowsTheActiveWarningCountOnActive()
    {
        await _users.SelectTabAsync("Active");

        var row = _users.UserRowById(WarnedMemberId);
        await Expect(row).ToBeVisibleAsync();
        await Expect(_users.WarningsCellOf(row)).ToHaveTextAsync(_activeWarningCount.ToString());
    }

    [Test]
    public async Task WarningsCell_ShowsTheActiveWarningCountOnTagged()
    {
        await _users.SelectTabAsync("Tagged");

        var row = _users.UserRowById(WarnedMemberId);
        await Expect(row).ToBeVisibleAsync();
        await Expect(_users.WarningsCellOf(row)).ToHaveTextAsync(_activeWarningCount.ToString());
    }
}
