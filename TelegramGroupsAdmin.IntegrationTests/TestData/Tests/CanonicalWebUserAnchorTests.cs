using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Testing.Golden;

namespace TelegramGroupsAdmin.IntegrationTests.TestData.Tests;

/// <summary>
/// Pins the canonical web users E2E logs in as: id, email, permission, status, TOTP flag.
/// A canonical regeneration that changes any of these must fail here, not in E2E.
/// </summary>
[TestFixture]
public class CanonicalWebUserAnchorTests
{
    private MigrationTestHelper _db = null!;

    [SetUp]
    public async Task SetUp()
    {
        _db = new MigrationTestHelper();
        await _db.CreateDatabaseFromGoldenTemplateAsync();
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    [TestCase(GoldenDatasetConstants.WebUsers.OwnerId, GoldenDatasetConstants.WebUsers.OwnerEmail, 2, true)]
    [TestCase(GoldenDatasetConstants.WebUsers.AdminId, GoldenDatasetConstants.WebUsers.AdminEmail, 0, true)]
    [TestCase(GoldenDatasetConstants.WebUsers.GlobalAdminId, GoldenDatasetConstants.WebUsers.GlobalAdminEmail, 1, true)]
    [TestCase(GoldenDatasetConstants.WebUsers.NoTotpGlobalAdminId, GoldenDatasetConstants.WebUsers.NoTotpGlobalAdminEmail, 1, false)]
    [TestCase(GoldenDatasetConstants.WebUsers.NoTotpAdminId, GoldenDatasetConstants.WebUsers.NoTotpAdminEmail, 0, false)]
    public async Task Anchor_HasExpectedShape(string id, string email, int permissionLevel, bool totpEnabled)
    {
        await using var ctx = _db.GetDbContext();
        var user = await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(user.Email, Is.EqualTo(email));
            Assert.That(user.PermissionLevel, Is.EqualTo(permissionLevel));
            Assert.That((int)user.Status, Is.EqualTo(1), "anchor must be active");
            Assert.That(user.TotpEnabled, Is.EqualTo(totpEnabled));
            Assert.That(user.SecurityStamp, Is.EqualTo(GoldenDatasetConstants.WebUsers.SecurityStamp));
        }
    }

    /// <summary>Canonical edit 2026-10-01: rerun@ carries the Disabled state (Enable-user E2E coverage).</summary>
    [Test]
    public async Task DisabledAnchor_IsADisabledInactiveAdmin()
    {
        await using var ctx = _db.GetDbContext();
        var user = await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == GoldenDatasetConstants.WebUsers.DisabledAdminId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(user.Email, Is.EqualTo(GoldenDatasetConstants.WebUsers.DisabledAdminEmail));
            Assert.That((int)user.Status, Is.EqualTo(GoldenDatasetConstants.WebUsers.DisabledAdminStatus));
            Assert.That(user.IsActive, Is.False);
            Assert.That(user.PermissionLevel, Is.Zero, "Admin");
            Assert.That(user.LockedUntil, Is.Null);
        }
    }
}
