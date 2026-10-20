using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.TestData.Tests;

/// <summary>
/// Guards the canonical dataset against a message whose author has no telegram_users row.
/// The messages.user_id foreign key makes that state impossible, so canonical must not contain it.
/// </summary>
[TestFixture]
public class CanonicalMessageAuthorTests
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

    [Test]
    public async Task EveryCanonicalMessage_HasAnAuthorRow()
    {
        await using var ctx = _db.GetDbContext();

        var orphans = await ctx.Database.SqlQueryRaw<int>(
            "SELECT count(*)::int AS \"Value\" FROM messages m WHERE NOT EXISTS (SELECT 1 FROM telegram_users u WHERE u.telegram_user_id = m.user_id)")
            .SingleAsync();

        Assert.That(orphans, Is.EqualTo(0), "every canonical message must have a telegram_users row for its author");
    }
}
