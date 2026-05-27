using Npgsql;
using NUnit.Framework;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Migrations;

[TestFixture]
public class DropUsernameBlacklistActorColumnsMigrationTests
{
    [Test]
    public async Task UsernameBlacklist_HasNoActorColumns_NoCheckConstraint_NoFks()
    {
        using var helper = new MigrationTestHelper();
        await helper.CreateDatabaseFromEmptyTemplateAsync();

        await using var connection = new NpgsqlConnection(helper.ConnectionString);
        await connection.OpenAsync();

        var columns = new List<string>();
        await using (var cmd = new NpgsqlCommand(
            @"SELECT column_name
              FROM information_schema.columns
              WHERE table_schema = 'public' AND table_name = 'username_blacklist'
              ORDER BY column_name",
            connection))
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                columns.Add(reader.GetString(0));
        }

        Assert.That(columns, Does.Not.Contain("web_user_id"));
        Assert.That(columns, Does.Not.Contain("telegram_user_id"));
        Assert.That(columns, Does.Not.Contain("system_identifier"));

        Assert.That(columns, Does.Contain("id"));
        Assert.That(columns, Does.Contain("pattern"));
        Assert.That(columns, Does.Contain("enabled"));
        Assert.That(columns, Does.Contain("created_at"));
        Assert.That(columns, Does.Contain("notes"));

        var constraints = new List<string>();
        await using (var cmd = new NpgsqlCommand(
            @"SELECT conname
              FROM pg_constraint
              WHERE conrelid = 'public.username_blacklist'::regclass",
            connection))
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                constraints.Add(reader.GetString(0));
        }

        Assert.That(constraints, Does.Not.Contain("CK_username_blacklist_exclusive_actor"));
        Assert.That(constraints, Does.Not.Contain("FK_username_blacklist_telegram_users_telegram_user_id"));
        Assert.That(constraints, Does.Not.Contain("FK_username_blacklist_users_web_user_id"));
    }
}
