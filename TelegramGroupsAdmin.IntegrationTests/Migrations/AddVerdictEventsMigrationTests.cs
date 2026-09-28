using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using TelegramGroupsAdmin.Data.Migrations;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Migrations;

/// <summary>
/// Backfill correctness for AddVerdictEvents: one synthetic legacy row per backfill branch,
/// migrated from the previous migration. Synthetic rows are allowed here (migration fixture).
/// </summary>
[TestFixture]
public class AddVerdictEventsMigrationTests
{
    internal const string PreviousMigration = "20260925190056_AddBanCelebrationSubscribers";
    private MigrationTestHelper _helper = null!;

    // ids >= 100000 so the identity sequence (used by the migration's INSERTs) never collides.
    // Shared with VerdictMigrationChainTests, which runs the full prod chain over the same rows.
    internal const string Seed = """
        INSERT INTO telegram_users (telegram_user_id, is_trusted, bot_dm_enabled, first_seen_at, last_seen_at, created_at, updated_at)
        VALUES (9000000000900, false, false, now(), now(), now(), now());

        INSERT INTO content_detection_configs (id, chat_id, config_json, last_updated)
        VALUES (100001, -1002, '{"ReviewQueueThreshold": 3.0}', now());

        INSERT INTO messages (message_id, user_id, chat_id, "timestamp")
        SELECT g, 9000000000900, -1001, now() - interval '1 day' FROM generate_series(1, 40) g;
        INSERT INTO messages (message_id, user_id, chat_id, "timestamp") VALUES
            (1, 9000000000900, -1002, now()),
            (-1, 0, 0, now()), (-2, 0, 0, now()), (-3, 0, 0, now()), (-4, 0, 0, now());

        INSERT INTO detection_results (id, message_id, chat_id, detected_at, detection_source, detection_method,
            score, net_score, reason, system_identifier, web_user_id, telegram_user_id, used_for_training, edit_version, check_results_json)
        VALUES
        -- scans
        (100001, 1, -1001, '2026-01-01T00:00:00Z', 'auto', 'ChannelReply', 0.8, 0.8, 'No spam detected', 'auto_detection', NULL, NULL, false, 0,
            '{"Checks":[{"CheckName":13,"Score":0.8,"Abstained":false}]}'),
        (100002, 2, -1001, '2026-01-01T00:00:00Z', 'auto', 'Similarity, OpenAI', 2.3, 2.3, 'AI confirmed spam: AI: Review - borderline', 'auto_detection', NULL, NULL, false, 0,
            '{"Checks":[{"CheckName":2,"Score":3.5,"Abstained":false},{"CheckName":6,"Score":2.3,"Abstained":false}]}'),
        (100003, 3, -1001, '2026-01-01T00:00:00Z', 'auto', 'StopWords, OpenAI', 4.5, 4.5, 'AI confirmed spam', 'auto_detection', NULL, NULL, true, 0,
            '{"Checks":[{"CheckName":0,"Score":3,"Abstained":false},{"CheckName":6,"Score":4.5,"Abstained":false}]}'),
        (100004, 4, -1001, '2026-01-01T00:00:00Z', 'auto', 'StopWords, OpenAI', 3.0, 3.0, 'AI confirmed spam', 'auto_detection', NULL, NULL, false, 0,
            '{"Checks":[{"CheckName":0,"Score":3,"Abstained":false},{"CheckName":6,"Score":3.0,"Abstained":false}]}'),
        (100005, 5, -1001, '2026-01-01T00:00:00Z', 'auto', 'StopWords, OpenAI', 0, 0, 'OpenAI vetoed spam', 'auto_detection', NULL, NULL, true, 0,
            '{"Checks":[{"CheckName":0,"Score":3,"Abstained":false},{"CheckName":6,"Score":0,"Abstained":false}]}'),
        (100006, 6, -1001, '2026-01-01T00:00:00Z', 'auto', 'UrlBlocklist', 5, 5, 'Hard block policy violation', 'auto_detection', NULL, NULL, true, 0,
            '{"Checks":[{"CheckName":8,"Score":5,"Abstained":false}]}'),
        (100007, 1, -1002, '2026-01-01T00:00:00Z', 'auto', 'Bayes', 2.8, 2.8, 'Additive score', 'auto_detection', NULL, NULL, false, 0,
            '{"Checks":[{"CheckName":3,"Score":2.8,"Abstained":false}]}'),
        -- pre-hotfix (2025-11-23) OpenAI veto encoding: Abstained=true, confidence in Score
        (100019, 15, -1001, '2025-11-11T18:17:12Z', 'auto', 'StopWords, Bayes, OpenAI', 4.5, 0, 'OpenAI vetoed spam: legitimate question', 'auto_detection', NULL, NULL, true, 0,
            '{"Checks":[{"CheckName":0,"Score":0.5,"Abstained":false},{"CheckName":6,"Score":4.5,"Abstained":true,"Details":"OpenAI vetoed spam: legitimate question"}]}'),
        (100020, 16, -1001, '2025-11-11T18:17:12Z', 'auto', 'Bayes, OpenAI', 4.5, 3.0, 'OpenAI vetoed spam: on topic', 'auto_detection', NULL, NULL, true, 0,
            '{"Checks":[{"CheckName":3,"Score":3.0,"Abstained":false},{"CheckName":6,"Score":4.5,"Abstained":true,"Details":"OpenAI vetoed spam: on topic"}]}'),
        (100021, 17, -1001, '2025-11-11T18:17:12Z', 'auto', 'Bayes, OpenAI', 0.5, 0.5, 'No spam detected', 'auto_detection', NULL, NULL, false, 0,
            '{"Checks":[{"CheckName":3,"Score":0.5,"Abstained":false},{"CheckName":6,"Score":4.5,"Abstained":true,"Details":"OpenAI error: timeout"}]}'),
        -- file scans
        (100008, 7, -1001, '2026-01-01T00:00:00Z', 'file_scan', 'FileScanningCheck', 5, 5, 'Malware', 'file_scanner', NULL, NULL, false, 0, NULL),
        (100009, 8, -1001, '2026-01-01T00:00:00Z', 'file_scan', 'FileScanningCheck', 0, 0, 'Clean', 'file_scanner', NULL, NULL, false, 0, NULL),
        -- manual decisions (source recovered from reason)
        (100010, 9,  -1001, '2026-01-01T00:00:00Z', 'manual', 'Manual', 5, 5,  'Manually marked as spam by admin via UI', NULL, NULL, 9000000000900, false, 0, NULL),
        (100011, 10, -1001, '2026-01-01T00:00:00Z', 'manual', 'Manual', 0, -5, 'Manually marked as ham (not spam) by admin - false positive correction', NULL, NULL, 9000000000900, false, 0, NULL),
        (100012, 11, -1001, '2026-01-01T00:00:00Z', 'manual', 'Manual', 5, 5,  'Spam detected via /spam command in chat X', NULL, NULL, 9000000000900, false, 0, NULL),
        (100013, 12, -1001, '2026-01-01T00:00:00Z', 'manual', 'Manual', 5, 5,  'Report #7 - spam/abuse', NULL, NULL, 9000000000900, false, 0, NULL),
        (100014, 13, -1001, '2026-01-01T00:00:00Z', 'manual', 'Manual', 5, 5,  'Marked as spam by moderator', 'auto_detection', NULL, NULL, false, 0, NULL),
        -- chat-0 page + imports
        (100015, -1, 0, '2026-01-01T00:00:00Z', 'manual', 'Manual', 5, 5,  'Manually added training sample', 'System', NULL, NULL, true, 0, NULL),
        (100016, -2, 0, '2026-01-01T00:00:00Z', 'manual', 'Manual', 5, -5, 'Manually added training sample', 'System', NULL, NULL, false, 0, NULL),
        (100017, -3, 0, '2026-01-01T00:00:00Z', 'tg-spam-import', 'Manual', 5, 5, 'Manual label - spam', 'System', NULL, NULL, true, 0, NULL),
        (100018, -4, 0, '2026-01-01T00:00:00Z', 'tg-spam-import', 'Manual', 5, 5, 'Manual label - ham', 'System', NULL, NULL, true, 0, NULL),
        -- view: equal detected_at, higher id wins
        (100030, 30, -1001, '2026-02-01T00:00:00Z', 'auto', 'Bayes', 4.5, 4.5, 'x', 'auto_detection', NULL, NULL, true, 0, '{"Checks":[{"CheckName":3,"Score":4.5,"Abstained":false}]}'),
        (100031, 30, -1001, '2026-02-01T00:00:00Z', 'auto', 'Bayes', 0, 0, 'x', 'auto_detection', NULL, NULL, false, 1, '{"Checks":[{"CheckName":3,"Score":0,"Abstained":true}]}'),
        -- view: FileScan newer than the scan is ignored
        (100040, 40, -1001, '2026-02-01T00:00:00Z', 'auto', 'Bayes', 0, 0, 'x', 'auto_detection', NULL, NULL, false, 0, '{"Checks":[]}'),
        (100041, 40, -1001, '2026-02-02T00:00:00Z', 'file_scan', 'FileScanningCheck', 5, 5, 'Malware', 'file_scanner', NULL, NULL, false, 0, NULL);

        INSERT INTO training_labels (message_id, chat_id, label, labeled_by_user_id, labeled_at, reason) VALUES
        (3,  -1001, 0, NULL,           '2026-01-02T00:00:00Z', 'Auto-detected spam'),        -- auto-ban: no manual row
        (10, -1001, 1, 9000000000900, '2026-01-02T00:00:00Z', 'Marked ham'),                 -- matches manual row 100011
        (14, -1001, 0, 9000000000900, '2026-01-02T00:00:00Z', 'Marked spam');                -- no manual row
        """;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _helper = new MigrationTestHelper();
        await _helper.CreateDatabaseAndMigrateToAsync(PreviousMigration);
        await _helper.ExecuteSqlAsync(Seed);
        await _helper.ApplyNextMigrationAsync(typeof(AddVerdictEvents).GetCustomAttribute<MigrationAttribute>()!.Id);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown() => _helper.Dispose();

    private async Task<(int Source, int Classification)> RowAsync(long id)
    {
        await using var conn = new NpgsqlConnection(_helper.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT source, classification FROM detection_results WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True, $"row {id} missing");
        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    [TestCase(100001, 0, 3, TestName = "Pipeline 0.8 → ContentScan/ImplicitHam")]
    [TestCase(100002, 0, 5, TestName = "AI review 2.3 → ContentScan/UntrainedHam")]
    [TestCase(100003, 0, 2, TestName = "AI 4.5 trained → ContentScan/ImplicitSpam")]
    [TestCase(100004, 0, 4, TestName = "AI 3.0 untrained → ContentScan/UntrainedSpam")]
    [TestCase(100005, 0, 3, TestName = "AI veto → ContentScan/ImplicitHam")]
    [TestCase(100006, 0, 2, TestName = "Hard block → ContentScan/ImplicitSpam")]
    [TestCase(100007, 0, 3, TestName = "Per-chat threshold 3.0 makes 2.8 ham")]
    [TestCase(100019, 0, 3, TestName = "Pre-hotfix OpenAI veto → ContentScan/ImplicitHam")]
    [TestCase(100020, 0, 3, TestName = "Pre-hotfix OpenAI veto beats a score over the threshold")]
    [TestCase(100021, 0, 3, TestName = "Abstained OpenAI with a score but no veto text stays abstained")]
    [TestCase(100008, 1, 4, TestName = "Infected file → FileScan/UntrainedSpam")]
    [TestCase(100009, 1, 5, TestName = "Clean file → FileScan/UntrainedHam")]
    [TestCase(100010, 11, 0, TestName = "Web mark spam")]
    [TestCase(100011, 12, 1, TestName = "Web mark ham")]
    [TestCase(100012, 13, 0, TestName = "/spam command")]
    [TestCase(100013, 14, 0, TestName = "Review spam")]
    [TestCase(100014, 99, 0, TestName = "Unrecoverable manual → LegacyManual")]
    [TestCase(100015, 16, 0, TestName = "Training Data page spam")]
    [TestCase(100016, 16, 1, TestName = "Training Data page ham")]
    [TestCase(100017, 18, 0, TestName = "Import spam")]
    [TestCase(100018, 18, 1, TestName = "Import label text wins over score sign")]
    public async Task Backfill_ClassifiesLegacyRow(long id, int expectedSource, int expectedClassification)
    {
        var (source, classification) = await RowAsync(id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(source, Is.EqualTo(expectedSource));
            Assert.That(classification, Is.EqualTo(expectedClassification));
        }
    }

    [TestCase(100019, true, TestName = "Pre-hotfix veto check rewritten to the veto encoding")]
    [TestCase(100020, true, TestName = "Pre-hotfix veto check over threshold rewritten")]
    [TestCase(100021, false, TestName = "Abstained OpenAI check without veto text left alone")]
    public async Task Backfill_LegacyVetoRepair(long id, bool expectRepaired)
    {
        await using var conn = new NpgsqlConnection(_helper.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT (c->>'Abstained')::boolean, (c->>'Score')::double precision,
                   COALESCE((d.properties->>'repaired_legacy_veto')::boolean, false),
                   COALESCE((d.properties->>'backfilled')::boolean, false)
            FROM detection_results d, jsonb_array_elements(d.check_results_json->'Checks') c
            WHERE d.id = @id AND (c->>'CheckName')::int = 6
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True, $"row {id} has no OpenAI check");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reader.GetBoolean(0), Is.EqualTo(!expectRepaired), "Abstained");
            Assert.That(reader.GetDouble(1), Is.EqualTo(expectRepaired ? 0 : 4.5), "Score");
            Assert.That(reader.GetBoolean(2), Is.EqualTo(expectRepaired), "repaired_legacy_veto marker");
            Assert.That(reader.GetBoolean(3), Is.True, "backfilled marker survives the repair marker");
        }
    }

    [Test]
    public async Task Backfill_ExcludedPageRow_GainsTrainingExcludeEvent()
    {
        var count = await _helper.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM detection_results WHERE message_id = -2 AND chat_id = 0 AND source = 17 AND classification = 5");
        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public async Task Backfill_AutoBanLabel_BecomesAutoBanDecision()
    {
        var count = await _helper.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM detection_results WHERE message_id = 3 AND chat_id = -1001 AND source = 10 AND classification = 0 AND system_identifier = 'auto_detection'");
        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public async Task Backfill_LabelWithMatchingManualRow_IsNotDuplicated()
    {
        var count = await _helper.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM detection_results WHERE message_id = 10 AND chat_id = -1001 AND source <> 0");
        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public async Task Backfill_UserLabelWithoutManualRow_BecomesLegacyManualDecision()
    {
        var count = await _helper.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM detection_results WHERE message_id = 14 AND chat_id = -1001 AND source = 99 AND classification = 0 AND telegram_user_id = 9000000000900");
        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public async Task View_EqualDetectedAt_HigherIdWins()
    {
        var verdictId = await _helper.ExecuteScalarAsync<long>(
            "SELECT verdict_id FROM message_verdicts WHERE chat_id = -1001 AND message_id = 30");
        Assert.That(verdictId, Is.EqualTo(100031));
    }

    [Test]
    public async Task View_IgnoresNewerFileScan()
    {
        var verdictId = await _helper.ExecuteScalarAsync<long>(
            "SELECT verdict_id FROM message_verdicts WHERE chat_id = -1001 AND message_id = 40");
        Assert.That(verdictId, Is.EqualTo(100040));
    }

    [Test]
    public async Task View_LabelNewerThanScan_Wins()
    {
        var source = await _helper.ExecuteScalarAsync<int>(
            "SELECT source FROM message_verdicts WHERE chat_id = -1001 AND message_id = 3");
        Assert.That(source, Is.EqualTo(10));
    }

    [Test]
    public async Task View_MessageWithoutRows_IsUnscannedHam()
    {
        await using var conn = new NpgsqlConnection(_helper.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT classification, is_spam, verdict_id IS NULL FROM message_verdicts WHERE chat_id = -1001 AND message_id = 20", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reader.GetInt32(0), Is.EqualTo(6));
            Assert.That(reader.GetBoolean(1), Is.False);
            Assert.That(reader.GetBoolean(2), Is.True);
        }
    }
}
