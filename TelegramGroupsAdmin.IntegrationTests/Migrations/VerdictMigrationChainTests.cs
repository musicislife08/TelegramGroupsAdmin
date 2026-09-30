using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using TelegramGroupsAdmin.Data.Migrations;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Migrations;

/// <summary>
/// The full prod migration chain over legacy data: AddVerdictEvents, UpdateDetectionAnalyticsViews
/// and DropLegacyVerdictColumns applied in turn over the legacy seed of
/// <see cref="AddVerdictEventsMigrationTests"/>. The final migration makes source/classification
/// NOT NULL, regenerates is_spam from classification and adds the source/classification CHECK, so
/// any legacy row the backfill left inconsistent fails here. Synthetic rows are allowed (migration fixture).
/// </summary>
[TestFixture]
public class VerdictMigrationChainTests
{
    private MigrationTestHelper _helper = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _helper = new MigrationTestHelper();
        await _helper.CreateDatabaseAndMigrateToAsync(AddVerdictEventsMigrationTests.PreviousMigration);
        await _helper.ExecuteSqlAsync(AddVerdictEventsMigrationTests.Seed);
        await _helper.ApplyNextMigrationAsync(MigrationId<AddVerdictEvents>());
    }

    [OneTimeTearDown]
    public void OneTimeTearDown() => _helper.Dispose();

    private static string MigrationId<T>() where T : Migration =>
        typeof(T).GetCustomAttribute<MigrationAttribute>()!.Id;

    [Test, Order(1)]
    public void Chain_AppliesUpdateDetectionAnalyticsViewsThenDropLegacyVerdictColumns()
    {
        Assert.DoesNotThrowAsync(() => _helper.ApplyNextMigrationAsync(MigrationId<UpdateDetectionAnalyticsViews>()),
            "UpdateDetectionAnalyticsViews over legacy data");
        Assert.DoesNotThrowAsync(() => _helper.ApplyNextMigrationAsync(MigrationId<DropLegacyVerdictColumns>()),
            "DropLegacyVerdictColumns over legacy data");
    }

    [Test, Order(2)]
    public async Task AfterChain_EveryRowHasSourceAndClassification()
    {
        var missing = await _helper.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM detection_results WHERE source IS NULL OR classification IS NULL");
        Assert.That(missing, Is.Zero);
    }

    [Test, Order(2)]
    public async Task AfterChain_GeneratedIsSpamMatchesClassification()
    {
        var total = await _helper.ExecuteScalarAsync<long>("SELECT count(*) FROM detection_results");
        var mismatched = await _helper.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM detection_results WHERE is_spam IS DISTINCT FROM (classification IN (0, 2, 4))");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(total, Is.GreaterThan(0), "the legacy seed must have produced rows");
            Assert.That(mismatched, Is.Zero);
        }
    }

    // Classification ints: 0 ExplicitSpam, 1 ExplicitHam, 2 ImplicitSpam, 3 ImplicitHam, 4 UntrainedSpam, 5 UntrainedHam, 6 Unscanned
    [TestCase(-1001, 2, 5, false, TestName = "AI review 2.3 scan stays UntrainedHam")]
    [TestCase(-1001, 3, 0, true, TestName = "Auto-ban label stays ExplicitSpam")]
    [TestCase(-1001, 4, 4, true, TestName = "Untrained AI spam stays UntrainedSpam")]
    [TestCase(-1001, 10, 1, false, TestName = "Web mark ham stays ExplicitHam")]
    [TestCase(-1001, 14, 0, true, TestName = "User label without manual row stays ExplicitSpam")]
    [TestCase(-1001, 20, 6, false, TestName = "Message without rows stays Unscanned")]
    [TestCase(-1002, 1, 3, false, TestName = "Per-chat threshold ham stays ImplicitHam")]
    [Order(2)]
    public async Task AfterChain_MessageVerdictsResolvesSeededMessages(long chatId, int messageId, int expectedClassification, bool expectedIsSpam)
    {
        await using var conn = new NpgsqlConnection(_helper.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT classification, is_spam FROM message_verdicts WHERE chat_id = @chat AND message_id = @msg", conn);
        cmd.Parameters.AddWithValue("chat", chatId);
        cmd.Parameters.AddWithValue("msg", messageId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True, $"message ({chatId}, {messageId}) missing from message_verdicts");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reader.GetInt32(0), Is.EqualTo(expectedClassification));
            Assert.That(reader.GetBoolean(1), Is.EqualTo(expectedIsSpam));
        }
    }

    /// <summary>
    /// Runs last: reverting DropLegacyVerdictColumns restores the legacy columns without leaving
    /// the temporary backfill defaults behind (the columns had none before Up).
    /// </summary>
    [Test, Order(3)]
    public async Task DropLegacyVerdictColumnsDown_LeavesNoLegacyColumnDefaults()
    {
        await _helper.ApplyNextMigrationAsync(MigrationId<UpdateDetectionAnalyticsViews>());

        var defaults = await _helper.ExecuteScalarAsync<long>("""
            SELECT count(*) FROM information_schema.columns
            WHERE table_name = 'detection_results'
              AND column_name IN ('detection_source', 'used_for_training', 'net_score')
              AND column_default IS NOT NULL
            """);
        var columns = await _helper.ExecuteScalarAsync<long>("""
            SELECT count(*) FROM information_schema.columns
            WHERE table_name = 'detection_results'
              AND column_name IN ('detection_source', 'used_for_training', 'net_score')
            """);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(columns, Is.EqualTo(3), "Down must restore the legacy columns");
            Assert.That(defaults, Is.Zero);
        }
    }

    /// <summary>Runs after the Down above: the seeded legacy labels come back with their labels.</summary>
    [TestCase(3, -1001L, 0)]
    [TestCase(10, -1001L, 1)]
    [TestCase(14, -1001L, 0)]
    [Order(4)]
    public async Task DropLegacyVerdictColumnsDown_RebuildsTrainingLabels(int messageId, long chatId, int expectedLabel)
    {
        var label = await _helper.ExecuteScalarAsync<short?>(
            $"SELECT label FROM training_labels WHERE message_id = {messageId} AND chat_id = {chatId}");
        Assert.That(label, Is.EqualTo((short)expectedLabel));
    }

    /// <summary>Runs after the Down above: a training-page row that was removed from training stays excluded.</summary>
    [Test, Order(4)]
    public async Task DropLegacyVerdictColumnsDown_KeepsTrainingExclusions()
    {
        var used = await _helper.ExecuteScalarAsync<bool>(
            "SELECT used_for_training FROM detection_results WHERE id = 100016");
        Assert.That(used, Is.False);
    }
}
