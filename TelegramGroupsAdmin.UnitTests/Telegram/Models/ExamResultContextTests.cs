using System.Text.Json;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Models;

[TestFixture]
public class ExamResultContextTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Test]
    public void Outcome_SerializesAsInt()
    {
        var context = new ExamResultContext { UserId = 1, Outcome = ExamOutcome.Passed };

        var json = JsonSerializer.Serialize(context, JsonOptions);

        Assert.That(json, Does.Contain("\"outcome\":1"));
        Assert.That(json, Does.Not.Contain("Passed"));
    }

    [Test]
    public void Outcome_MissingAfterPreOutcomeBackupRestore_DeserializesAsFailed()
    {
        // The migration stamps every live row, so this key is never absent through the
        // normal path. It CAN be absent after restoring a backup taken before ExamOutcome
        // existed: BackupService re-inserts DTO rows verbatim and migrations don't re-run
        // on restore. Every exam row from that era is a failure, so the enum default (0)
        // must be Failed.
        const string preOutcomeBackupJson = """{"userId":42,"score":50,"passingThreshold":80}""";

        var context = JsonSerializer.Deserialize<ExamResultContext>(preOutcomeBackupJson, JsonOptions);

        Assert.That(context!.Outcome, Is.EqualTo(ExamOutcome.Failed));
    }
}
