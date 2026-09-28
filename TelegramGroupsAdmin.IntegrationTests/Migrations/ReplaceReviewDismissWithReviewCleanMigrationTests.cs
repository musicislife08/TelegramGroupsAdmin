using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using TelegramGroupsAdmin.Data.Migrations;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Migrations;

/// <summary>
/// ReplaceReviewDismissWithReviewClean up and down: pre-release ReviewDismiss (15) rows are dropped,
/// the analytics views count ReviewClean (19) as a human decision, and reverting maps ReviewClean rows
/// to WebMarkHam so the old CHECK holds. Migration fixture on an empty schema, so synthetic rows are allowed.
/// </summary>
[TestFixture]
public class ReplaceReviewDismissWithReviewCleanMigrationTests
{
    private MigrationTestHelper _helper = null!;

    private static string MigrationId<T>() where T : Migration =>
        typeof(T).GetCustomAttribute<MigrationAttribute>()!.Id;

    private static readonly string Previous = MigrationId<DropLegacyVerdictColumns>();
    private static readonly string Target = MigrationId<ReplaceReviewDismissWithReviewClean>();

    [SetUp]
    public async Task SetUp()
    {
        _helper = new MigrationTestHelper();
        await _helper.CreateDatabaseAndMigrateToAsync(Previous);
        await _helper.ExecuteSqlAsync("""
            INSERT INTO messages (message_id, user_id, chat_id, "timestamp") VALUES (1, 0, -1001, now());
            """);
    }

    [TearDown]
    public void TearDown() => _helper.Dispose();

    private Task InsertAsync(int source, int classification, string detectedAt = "now()") => _helper.ExecuteSqlAsync($"""
        INSERT INTO detection_results (message_id, chat_id, detected_at, detection_method, score, reason,
            system_identifier, edit_version, source, classification)
        VALUES (1, -1001, {detectedAt}, 'x', 1, 'x', 'integration-test', 0, {source}, {classification});
        """);

    [Test]
    public async Task Up_DropsPreReleaseReviewDismissRows()
    {
        await InsertAsync(0, 4);       // review-queued scan (UntrainedSpam)
        await InsertAsync(15, 3);      // pre-release dismiss (ImplicitHam)

        await _helper.ApplyNextMigrationAsync(Target);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _helper.ExecuteScalarAsync<long>("SELECT count(*) FROM detection_results WHERE source = 15"), Is.Zero);
            Assert.That(await _helper.ExecuteScalarAsync<int>(
                "SELECT classification FROM message_verdicts WHERE message_id = 1"), Is.EqualTo(4),
                "the dismiss was an acknowledgement: the scan's verdict stands");
        }
    }

    [Test]
    public async Task Up_AnalyticsViewsCountReviewCleanAsAHumanDecision()
    {
        await _helper.ApplyNextMigrationAsync(Target);
        await InsertAsync(0, 2, "now() - interval '1 minute'");   // scan: ImplicitSpam
        await InsertAsync(19, 1);                                 // review-queue Mark clean

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _helper.ExecuteScalarAsync<bool>(
                "SELECT is_false_positive FROM detection_accuracy WHERE message_id = 1"), Is.True);
            Assert.That(await _helper.ExecuteScalarAsync<long>(
                "SELECT sum(manual_count) FROM hourly_detection_stats"), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task Down_MapsReviewCleanToWebMarkHam()
    {
        await _helper.ApplyNextMigrationAsync(Target);
        await InsertAsync(19, 1);

        await _helper.ApplyNextMigrationAsync(Previous);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _helper.ExecuteScalarAsync<long>("SELECT count(*) FROM detection_results WHERE source = 19"), Is.Zero);
            Assert.That(await _helper.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM detection_results WHERE source = 12 AND classification = 1"), Is.EqualTo(1));
        }
    }
}
