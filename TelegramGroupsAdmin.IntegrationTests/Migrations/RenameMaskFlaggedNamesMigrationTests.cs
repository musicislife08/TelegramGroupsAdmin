using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using TelegramGroupsAdmin.Data.Migrations;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Migrations;

/// <summary>
/// RenameMaskFlaggedNames up and down: the profileScan JSON key maskExplicitUsername becomes
/// maskFlaggedNames (value kept) and explicitUsernameRedactionText is dropped. Migration fixture on
/// an empty schema, so synthetic rows are allowed.
/// </summary>
[TestFixture]
public class RenameMaskFlaggedNamesMigrationTests
{
    private MigrationTestHelper _helper = null!;

    private static string MigrationId<T>() where T : Migration =>
        typeof(T).GetCustomAttribute<MigrationAttribute>()!.Id;

    private static readonly string Previous = MigrationId<ReplaceReviewDismissWithReviewClean>();
    private static readonly string Target = MigrationId<RenameMaskFlaggedNames>();

    private const string ProfileScanPath = "welcome_config #> '{joinSecurity,profileScan}'";

    [SetUp]
    public async Task SetUp()
    {
        _helper = new MigrationTestHelper();
        await _helper.CreateDatabaseAndMigrateToAsync(Previous);
    }

    [TearDown]
    public void TearDown() => _helper.Dispose();

    private Task InsertConfigAsync(long chatId, string welcomeJson) => _helper.ExecuteSqlAsync($$"""
        INSERT INTO configs (chat_id, welcome_config, created_at)
        VALUES ({{chatId}}, '{{welcomeJson}}'::jsonb, now());
        """);

    private async Task<string?> ProfileScanAsync(long chatId) => await _helper.ExecuteScalarAsync<string>(
        $"SELECT ({ProfileScanPath})::text FROM configs WHERE chat_id = {chatId}");

    [Test]
    public async Task Up_RenamesKeyKeepsValueAndDropsRedactionText()
    {
        await InsertConfigAsync(-1001,
            """{"joinSecurity":{"profileScan":{"enabled":true,"maskExplicitUsername":false,"explicitUsernameRedactionText":"x"}}}""");

        await _helper.ApplyNextMigrationAsync(Target);

        Assert.That(await _helper.ExecuteScalarAsync<bool>(
            $"SELECT {ProfileScanPath} = '{{\"enabled\":true,\"maskFlaggedNames\":false}}'::jsonb FROM configs WHERE chat_id = -1001"),
            Is.True);
    }

    [Test]
    public async Task Up_LeavesRowsWithoutProfileScanUntouched()
    {
        await InsertConfigAsync(-1002, """{"enabled":true}""");

        await _helper.ApplyNextMigrationAsync(Target);

        Assert.That(await _helper.ExecuteScalarAsync<bool>(
            "SELECT welcome_config = '{\"enabled\":true}'::jsonb FROM configs WHERE chat_id = -1002"), Is.True);
    }

    [Test]
    public async Task Down_RestoresOldKey()
    {
        await InsertConfigAsync(-1001,
            """{"joinSecurity":{"profileScan":{"enabled":true,"maskExplicitUsername":false}}}""");
        await _helper.ApplyNextMigrationAsync(Target);

        await _helper.ApplyNextMigrationAsync(Previous);

        Assert.That(await _helper.ExecuteScalarAsync<bool>(
            $"SELECT {ProfileScanPath} = '{{\"enabled\":true,\"maskExplicitUsername\":false}}'::jsonb FROM configs WHERE chat_id = -1001"),
            Is.True);
    }
}
