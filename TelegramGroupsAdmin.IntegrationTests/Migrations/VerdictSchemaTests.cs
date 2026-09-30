using Npgsql;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Migrations;

/// <summary>
/// Final verdict schema after DropLegacyVerdictColumns: is_spam generated from classification,
/// CHECK constraints tying source to classification, the view passing is_spam through, and the
/// legacy label/sample tables gone. Schema fixture on the empty template, so synthetic rows are allowed.
/// </summary>
[TestFixture]
public class VerdictSchemaTests
{
    private MigrationTestHelper _helper = null!;

    [SetUp]
    public async Task SetUp()
    {
        _helper = new MigrationTestHelper();
        await _helper.CreateDatabaseFromEmptyTemplateAsync();
        await _helper.ExecuteSqlAsync("""
            INSERT INTO messages (message_id, user_id, chat_id, "timestamp") VALUES (1, 0, -1001, now());
            """);
    }

    [TearDown]
    public void TearDown() => _helper.Dispose();

    private Task InsertAsync(int source, int classification) => _helper.ExecuteSqlAsync($"""
        INSERT INTO detection_results (message_id, chat_id, detected_at, detection_method, score, reason,
            system_identifier, edit_version, source, classification)
        VALUES (1, -1001, now(), 'x', 1, 'x', 'integration-test', 0, {source}, {classification});
        """);

    [TestCase(VerdictSource.ContentScan, VerdictClassification.ImplicitSpam)]
    [TestCase(VerdictSource.ContentScan, VerdictClassification.ImplicitHam)]
    [TestCase(VerdictSource.ContentScan, VerdictClassification.UntrainedSpam)]
    [TestCase(VerdictSource.ContentScan, VerdictClassification.UntrainedHam)]
    [TestCase(VerdictSource.AutoBan, VerdictClassification.ExplicitSpam)]
    [TestCase(VerdictSource.WebMarkHam, VerdictClassification.ExplicitHam)]
    [TestCase(VerdictSource.ReviewClean, VerdictClassification.ExplicitHam)]
    public async Task GeneratedIsSpam_MatchesCoreIsSpam(VerdictSource source, VerdictClassification classification)
    {
        await InsertAsync((int)source, (int)classification);
        var isSpam = await _helper.ExecuteScalarAsync<bool>("SELECT is_spam FROM detection_results ORDER BY id DESC LIMIT 1");
        Assert.That(isSpam, Is.EqualTo(classification.IsSpam()));
    }

    [TestCase(VerdictSource.WebMarkHam, VerdictClassification.ExplicitSpam)]
    [TestCase(VerdictSource.AutoBan, VerdictClassification.ImplicitSpam)]
    [TestCase(VerdictSource.ContentScan, VerdictClassification.ExplicitSpam)]
    [TestCase(VerdictSource.FileScan, VerdictClassification.ImplicitHam)]
    [TestCase(VerdictSource.ReviewClean, VerdictClassification.ImplicitHam)]
    [TestCase((VerdictSource)15, VerdictClassification.ImplicitHam)]
    public void CheckConstraint_RejectsInconsistentSourceAndClassification(VerdictSource source, VerdictClassification classification)
    {
        var ex = Assert.ThrowsAsync<PostgresException>(() => InsertAsync((int)source, (int)classification));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex!.SqlState, Is.EqualTo(PostgresErrorCodes.CheckViolation));
            Assert.That(ex.ConstraintName, Is.EqualTo("CK_detection_results_source_classification"));
        }
    }

    [Test]
    public void CheckConstraint_RejectsStoredUnscanned()
    {
        var ex = Assert.ThrowsAsync<PostgresException>(() => InsertAsync((int)VerdictSource.ContentScan, (int)VerdictClassification.Unscanned));
        Assert.That(ex!.SqlState, Is.EqualTo(PostgresErrorCodes.CheckViolation));
    }

    [Test]
    public async Task View_PassesIsSpamThrough()
    {
        await InsertAsync((int)VerdictSource.ContentScan, (int)VerdictClassification.UntrainedSpam);
        Assert.That(await _helper.ExecuteScalarAsync<bool>("SELECT is_spam FROM message_verdicts WHERE message_id = 1"), Is.True);
    }

    [TestCase("training_labels")]
    [TestCase("image_training_samples")]
    [TestCase("video_training_samples")]
    public async Task LegacyTables_AreGone(string table)
        => Assert.That(await _helper.ExecuteScalarAsync<long>($"SELECT count(*) FROM information_schema.tables WHERE table_name = '{table}'"), Is.Zero);
}
