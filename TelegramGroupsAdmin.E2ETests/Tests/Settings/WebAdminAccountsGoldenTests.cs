using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.E2ETests.PageObjects.Settings;
using TelegramGroupsAdmin.Testing.Golden;
using UserStatus = TelegramGroupsAdmin.Core.Models.UserStatus;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Settings;

/// <summary>
/// Per-user actions on the Web Admin Accounts page (/settings/system/accounts) as the canonical Owner,
/// against canonical web users as they are: the action menu writes are the assertion subjects, and
/// every UI change is read back from this test's clone. Shapes canonical cannot hold at rest (a live
/// lockout) live in <see cref="WebAdminAccountsLockedUserGoldenTests"/>.
/// </summary>
[TestFixture]
public class WebAdminAccountsGoldenTests : GoldenE2ETestBase
{
    private const string Admin = GoldenDatasetConstants.WebUsers.NoTotpAdminEmail;          // reshoot@, Admin, active
    private const string TotpGlobalAdmin = GoldenDatasetConstants.WebUsers.GlobalAdminEmail;  // ahead@, GlobalAdmin, TOTP on
    private const string DisabledAdmin = GoldenDatasetConstants.WebUsers.DisabledAdminEmail;  // rerun@, Disabled
    private const string DeletedAdmin = "deleted@example.com";                                // DeletedAdminId, Deleted

    private WebAdminAccountsPage _accounts = null!;

    protected override async Task ArrangeDataAsync(AppDbContext context)
    {
        // Guards for the rows whose shape the tests below rely on: a later canonical change fails here.
        var disabled = await context.Users.AsNoTracking()
            .Where(u => u.Id == GoldenDatasetConstants.WebUsers.DisabledAdminId)
            .Select(u => new { u.Email, u.Status, u.IsActive }).SingleAsync();
        Assert.That((int)disabled.Status, Is.EqualTo(GoldenDatasetConstants.WebUsers.DisabledAdminStatus), "canonical edit 2026-10-01: rerun@ must be Disabled");
        Assert.That(disabled.Email, Is.EqualTo(DisabledAdmin));
        Assert.That(disabled.IsActive, Is.False);

        var deleted = await context.Users.AsNoTracking()
            .Where(u => u.Id == GoldenDatasetConstants.WebUsers.DeletedAdminId)
            .Select(u => new { u.Email, u.Status }).SingleAsync();
        Assert.That((int)deleted.Status, Is.EqualTo(GoldenDatasetConstants.WebUsers.DeletedAdminStatus));
        Assert.That(deleted.Email, Is.EqualTo(DeletedAdmin));
    }

    [SetUp]
    public async Task OpenAccountsPageAsOwner()
    {
        _accounts = new WebAdminAccountsPage(Page);
        await LoginAsOwnerAsync();
        await _accounts.NavigateAsync();
        await _accounts.WaitForLoadAsync();
    }

    [Test]
    public async Task DisableUser_ShowsDisabledChipAndPersists()
    {
        await Expect(_accounts.UserStatusChip(Admin, "Active")).ToBeVisibleAsync();

        await _accounts.OpenActionMenuForUserAsync(Admin);
        await _accounts.ClickActionMenuItemAsync("Disable User");
        await _accounts.ConfirmDialogAsync();

        await Expect(_accounts.SnackbarWithText($"Disabled user {Admin}")).ToBeVisibleAsync();
        await Expect(_accounts.UserStatusChip(Admin, "Disabled")).ToBeVisibleAsync();

        var user = await ReadUserAsync(GoldenDatasetConstants.WebUsers.NoTotpAdminId);
        Assert.That(user.Status, Is.EqualTo(UserStatus.Disabled));
        Assert.That(user.IsActive, Is.False);
    }

    [Test]
    public async Task CancelDisable_LeavesTheUserActive()
    {
        await _accounts.OpenActionMenuForUserAsync(Admin);
        await _accounts.ClickActionMenuItemAsync("Disable User");
        await Expect(_accounts.DialogTitle).ToHaveTextAsync("Disable User");
        await _accounts.CancelDialogAsync();

        await Expect(_accounts.UserStatusChip(Admin, "Active")).ToBeVisibleAsync();
        await Expect(_accounts.Snackbar).ToHaveCountAsync(0);

        var user = await ReadUserAsync(GoldenDatasetConstants.WebUsers.NoTotpAdminId);
        Assert.That(user.Status, Is.EqualTo(UserStatus.Active));
        Assert.That(user.IsActive, Is.True);
    }

    [Test]
    public async Task EnableUser_ShowsActiveChipAndPersists()
    {
        // The default status filter (Active + Pending + Disabled) lists the disabled canonical Admin.
        await Expect(_accounts.UserStatusChip(DisabledAdmin, "Disabled")).ToBeVisibleAsync();

        await _accounts.OpenActionMenuForUserAsync(DisabledAdmin);
        await _accounts.ClickActionMenuItemAsync("Enable User"); // no confirmation dialog

        await Expect(_accounts.SnackbarWithText($"Enabled user {DisabledAdmin}")).ToBeVisibleAsync();
        await Expect(_accounts.UserStatusChip(DisabledAdmin, "Active")).ToBeVisibleAsync();

        var user = await ReadUserAsync(GoldenDatasetConstants.WebUsers.DisabledAdminId);
        Assert.That(user.Status, Is.EqualTo(UserStatus.Active));
        Assert.That(user.IsActive, Is.True);
    }

    [Test]
    public async Task DeleteUser_LeavesTheDefaultFilterAndPersists()
    {
        await Expect(_accounts.UserRow(Admin)).ToHaveCountAsync(1);

        await _accounts.OpenActionMenuForUserAsync(Admin);
        await _accounts.ClickActionMenuItemAsync("Delete User");
        await _accounts.ConfirmDialogAsync();

        // The snackbar is the presence check for the post-delete render; the row is gone from it.
        await Expect(_accounts.SnackbarWithText($"Deleted user {Admin}")).ToBeVisibleAsync();
        await Expect(_accounts.UserRow(Admin)).ToHaveCountAsync(0);

        // Adding Deleted to the status filter brings the row back with the Deleted chip.
        await _accounts.ToggleStatusFilterAsync("Deleted");
        await Expect(_accounts.UserStatusChip(Admin, "Deleted")).ToBeVisibleAsync();

        var user = await ReadUserAsync(GoldenDatasetConstants.WebUsers.NoTotpAdminId);
        Assert.That(user.Status, Is.EqualTo(UserStatus.Deleted));
        Assert.That(user.IsActive, Is.False);
    }

    [Test]
    public async Task RestoreUser_ShowsActiveChipAndPersists()
    {
        await _accounts.ToggleStatusFilterAsync("Deleted");
        await Expect(_accounts.UserStatusChip(DeletedAdmin, "Deleted")).ToBeVisibleAsync();

        await _accounts.OpenActionMenuForUserAsync(DeletedAdmin);
        await _accounts.ClickActionMenuItemAsync("Restore User");
        await _accounts.ConfirmDialogAsync();

        await Expect(_accounts.SnackbarWithText($"Restored user {DeletedAdmin}")).ToBeVisibleAsync();
        await Expect(_accounts.UserStatusChip(DeletedAdmin, "Active")).ToBeVisibleAsync();

        var user = await ReadUserAsync(GoldenDatasetConstants.WebUsers.DeletedAdminId);
        Assert.That(user.Status, Is.EqualTo(UserStatus.Active));
        Assert.That(user.IsActive, Is.True);
    }

    [Test]
    public async Task EditPermission_AdminToGlobalAdmin_UpdatesChipAndPersists()
    {
        await Expect(_accounts.UserPermissionChip(Admin)).ToHaveTextAsync("Admin");

        await _accounts.OpenActionMenuForUserAsync(Admin);
        await _accounts.ClickActionMenuItemAsync("Edit Permission");
        await Expect(_accounts.DialogTitle).ToHaveTextAsync("Edit Permission Level");
        await _accounts.SelectPermissionLevelAsync("GlobalAdmin");
        await _accounts.SaveDialogAsync();

        await Expect(_accounts.SnackbarWithText($"Updated {Admin} to GlobalAdmin")).ToBeVisibleAsync();
        await Expect(_accounts.UserPermissionChip(Admin)).ToHaveTextAsync("GlobalAdmin");

        var user = await ReadUserAsync(GoldenDatasetConstants.WebUsers.NoTotpAdminId);
        Assert.That(user.PermissionLevel, Is.EqualTo(1), "PermissionLevel.GlobalAdmin");
    }

    [Test]
    public async Task DisableTotp_SwitchesTheIconToWarningAndPersists()
    {
        // MudIcon Color=Success renders mud-success-text; the disabled state renders Color=Warning.
        await Expect(_accounts.UserTotpIcon(TotpGlobalAdmin)).ToHaveClassAsync(new Regex(@"\bmud-success-text\b"));

        await _accounts.OpenActionMenuForUserAsync(TotpGlobalAdmin);
        await _accounts.ClickActionMenuItemAsync("Disable TOTP");
        await _accounts.ConfirmDialogAsync();

        await Expect(_accounts.SnackbarWithText($"Disabled TOTP for {TotpGlobalAdmin}")).ToBeVisibleAsync();
        await Expect(_accounts.UserTotpIcon(TotpGlobalAdmin)).ToHaveClassAsync(new Regex(@"\bmud-warning-text\b"));

        var user = await ReadUserAsync(GoldenDatasetConstants.WebUsers.GlobalAdminId);
        Assert.That(user.TotpEnabled, Is.False);
        Assert.That(user.SecurityStamp, Is.Not.EqualTo(GoldenDatasetConstants.WebUsers.SecurityStamp), "the stamp rotates to end the user's sessions");
    }

    private async Task<(UserStatus Status, bool IsActive, int PermissionLevel, bool TotpEnabled, string SecurityStamp)> ReadUserAsync(string userId)
    {
        await using var ctx = CreateDbContext();
        var u = await ctx.Users.AsNoTracking().Where(x => x.Id == userId)
            .Select(x => new { x.Status, x.IsActive, x.PermissionLevel, x.TotpEnabled, x.SecurityStamp }).SingleAsync();
        return ((UserStatus)(int)u.Status, u.IsActive, u.PermissionLevel, u.TotpEnabled, u.SecurityStamp);
    }
}

/// <summary>
/// The locked-account shape: a lockout only counts while <c>locked_until</c> is ahead of NOW(), so the
/// canonical no-TOTP Admin is locked by <c>GoldenDataset.Mutate(...).LockWebUser</c> before the app starts.
/// </summary>
[TestFixture]
public class WebAdminAccountsLockedUserGoldenTests : GoldenE2ETestBase
{
    private const string LockedAdmin = GoldenDatasetConstants.WebUsers.NoTotpAdminEmail;

    protected override async Task ArrangeDataAsync(AppDbContext context)
    {
        await GoldenDataset.Mutate(context)
            .LockWebUser(GoldenDatasetConstants.WebUsers.NoTotpAdminId, TimeSpan.FromMinutes(30))
            .ApplyAsync();

        var lockedUntil = await context.Users.AsNoTracking()
            .Where(u => u.Id == GoldenDatasetConstants.WebUsers.NoTotpAdminId).Select(u => u.LockedUntil).SingleAsync();
        Assert.That(lockedUntil, Is.GreaterThan(DateTimeOffset.UtcNow), "the lock must still be live when the app reads it");
    }

    [Test]
    public async Task UnlockAccount_RemovesTheLockedChipAndPersists()
    {
        var accounts = new WebAdminAccountsPage(Page);
        await LoginAsOwnerAsync();
        await accounts.NavigateAsync();
        await accounts.WaitForLoadAsync();

        await Expect(accounts.UserLockedChip(LockedAdmin)).ToBeVisibleAsync();

        await accounts.OpenActionMenuForUserAsync(LockedAdmin);
        await accounts.ClickActionMenuItemAsync("Unlock Account");
        await Expect(accounts.Dialog).ToContainTextAsync("minutes remaining");
        await accounts.ConfirmDialogAsync();

        await Expect(accounts.SnackbarWithText($"Unlocked account for {LockedAdmin}")).ToBeVisibleAsync();
        await Expect(accounts.UserStatusChip(LockedAdmin, "Active")).ToBeVisibleAsync();
        await Expect(accounts.UserLockedChip(LockedAdmin)).ToHaveCountAsync(0);

        await using var ctx = CreateDbContext();
        var user = await ctx.Users.AsNoTracking()
            .Where(u => u.Id == GoldenDatasetConstants.WebUsers.NoTotpAdminId)
            .Select(u => new { u.LockedUntil, u.FailedLoginAttempts }).SingleAsync();
        Assert.That(user.LockedUntil, Is.Null);
        Assert.That(user.FailedLoginAttempts, Is.Zero);
    }
}
