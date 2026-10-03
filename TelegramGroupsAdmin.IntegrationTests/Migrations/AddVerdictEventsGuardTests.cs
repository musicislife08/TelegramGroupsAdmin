using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using TelegramGroupsAdmin.Data.Migrations;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Migrations;

/// <summary>
/// AddVerdictEvents' input guards: an unknown detection_source stops the migration instead of becoming
/// an explicit training decision, and a non-array check_results_json "Checks" is read as no checks.
/// Migration fixture on an empty schema, so synthetic rows are allowed.
/// </summary>
[TestFixture]
public class AddVerdictEventsGuardTests
{
    private MigrationTestHelper _helper = null!;

    private static readonly string Target = typeof(AddVerdictEvents).GetCustomAttribute<MigrationAttribute>()!.Id;

    [SetUp]
    public async Task SetUp()
    {
        _helper = new MigrationTestHelper();
        await _helper.CreateDatabaseAndMigrateToAsync(AddVerdictEventsMigrationTests.PreviousMigration);
        await _helper.ExecuteSqlAsync("""
            INSERT INTO messages (message_id, user_id, chat_id, "timestamp") VALUES (1, 0, -1001, now());
            """);
    }

    [TearDown]
    public void TearDown() => _helper.Dispose();

    private Task InsertAsync(string detectionSource, string checkResultsJson) => _helper.ExecuteSqlAsync($"""
        INSERT INTO detection_results (id, message_id, chat_id, detected_at, detection_source, detection_method,
            score, net_score, reason, system_identifier, used_for_training, edit_version, check_results_json)
        VALUES (1, 1, -1001, now(), '{detectionSource}', 'x', 3, 3, 'x', 'auto_detection', false, 0, {checkResultsJson});
        """);

    [Test]
    public async Task UnknownDetectionSource_StopsTheMigration()
    {
        await InsertAsync("automated", "NULL");

        var ex = Assert.ThrowsAsync<PostgresException>(() => _helper.ApplyNextMigrationAsync(Target));
        Assert.That(ex!.MessageText, Does.Contain("unknown detection_source"));
    }

    [Test]
    public async Task NonArrayChecks_AreReadAsNoChecks()
    {
        await InsertAsync("auto", """'{"Checks": {"not": "an array"}}'""");

        await _helper.ApplyNextMigrationAsync(Target);

        // No checks, net_score 3 >= default review threshold 2.5, not used for training → UntrainedSpam.
        Assert.That(await _helper.ExecuteScalarAsync<int>("SELECT classification FROM detection_results WHERE id = 1"),
            Is.EqualTo(4));
    }
}
