using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using TelegramGroupsAdmin.BackgroundJobs.Services.Backup;
using TelegramGroupsAdmin.BackgroundJobs.Services.Backup.Migrations;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.UnitTests.BackgroundJobs.Services.Backup;

/// <summary>
/// Backup rows are shaped the way the 3.0 export wrote them: keys are the snake_case of the DTO
/// property names (equal to the column names for detection_results / training_labels), jsonb string
/// properties (check_results_json) are raw JSON strings, and typed jsonb properties
/// (content_detection_configs.Config) are embedded objects with snake_case keys.
/// </summary>
[TestFixture]
public class Backup30To31VerdictMigrationTests
{
    private const long ChatId = -1001;
    private const long QuietChatId = -1002;
    private const long AdminId = 9;

    // The restore path's options (BackupService.RestoreTableAsync).
    private static readonly JsonSerializerOptions RestoreOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    private static object Row(object anonymous) => JsonSerializer.SerializeToElement(anonymous);

    private static object Scan(long id, int messageId, double netScore, bool usedForTraining, string checks, long chatId = ChatId) =>
        Row(new
        {
            id, message_id = messageId, chat_id = chatId, detected_at = "2026-01-01T00:00:00+00:00", detection_source = "auto",
            detection_method = "Bayes, OpenAI", is_spam = netScore >= 2.5, net_score = netScore, score = netScore,
            used_for_training = usedForTraining, reason = "scan", web_user_id = (string?)null, telegram_user_id = (long?)null,
            system_identifier = "auto_detection", check_results_json = checks, edit_version = 0
        });

    private static SystemBackup Build() => new()
    {
        Metadata = new BackupMetadata
        {
            Version = "3.0",
            TableCount = 5,
            Tables = ["detection_results", "training_labels", "image_training_samples", "video_training_samples", "content_detection_configs"]
        },
        Data = new Dictionary<string, List<object>>
        {
            ["content_detection_configs"] =
            [
                Row(new { id = 1, chat_id = 0L, config = new { review_queue_threshold = 2.5 } }),
                Row(new { id = 2, chat_id = QuietChatId, config = new { review_queue_threshold = 4.0 } })
            ],
            ["detection_results"] =
            [
                Scan(10, 1, 0.8, false, """{"Checks": [{"Score": 0.8, "CheckName": 13, "Abstained": false}]}"""),
                Scan(11, 2, 2.3, false, """{"Checks": [{"Score": 3.5, "CheckName": 2, "Abstained": false}, {"Score": 2.3, "CheckName": 6, "Abstained": false}]}"""),
                Row(new
                {
                    id = 12, message_id = 3, chat_id = ChatId, detected_at = "2026-01-01T00:00:00+00:00", detection_source = "manual",
                    detection_method = "Manual", is_spam = true, net_score = -5.0, score = 0.0, used_for_training = true,
                    reason = "Manually marked as ham (not spam) by admin", web_user_id = (string?)null, telegram_user_id = AdminId,
                    system_identifier = (string?)null, check_results_json = (string?)null, edit_version = 0
                }),
                Scan(13, 5, 3.0, true, """{"Checks": [{"Score": 3.0, "CheckName": 2, "Abstained": false}]}"""),
                Scan(14, 6, 9.0, true, """{"Checks": [{"Score": 9.0, "CheckName": 2, "Abstained": false}, {"Score": 0.0, "CheckName": 6, "Abstained": false}]}"""),
                Scan(15, 7, 1.0, false, """{"Checks": [{"Score": 5.0, "CheckName": 8, "Abstained": false}]}"""),
                Scan(16, 8, 3.0, false, """{"Checks": []}""", QuietChatId),
                Row(new
                {
                    id = 17, message_id = 9, chat_id = ChatId, detected_at = "2026-01-03T00:00:00+00:00", detection_source = "file_scan",
                    detection_method = "ClamAV", is_spam = true, net_score = 5.0, score = 5.0, used_for_training = false,
                    reason = "infected", system_identifier = "file_scanner", check_results_json = (string?)null, edit_version = 0
                }),
                Row(new
                {
                    id = 18, message_id = 10, chat_id = ChatId, detected_at = "2026-01-04T00:00:00.123456+00:00", detection_source = "tg-spam-import",
                    detection_method = "Import", is_spam = true, net_score = -1.0, score = 1.0, used_for_training = false,
                    reason = "Imported (label - spam)", web_user_id = "web-admin", telegram_user_id = (long?)null,
                    system_identifier = (string?)null, check_results_json = (string?)null, edit_version = 0
                }),
                Row(new
                {
                    id = 19, message_id = 11, chat_id = 0L, detected_at = "2026-01-05T00:00:00+00:00", detection_source = "manual",
                    detection_method = "Manual", is_spam = false, net_score = -5.0, score = 5.0, used_for_training = true,
                    reason = "Training sample", web_user_id = "web-admin", check_results_json = (string?)null, edit_version = 0
                })
            ],
            ["training_labels"] =
            [
                Row(new { message_id = 3, chat_id = ChatId, label = 1, labeled_by_user_id = AdminId, labeled_at = "2026-01-02T00:00:00+00:00", reason = (string?)null, audit_log_id = (long?)null }),
                Row(new { message_id = 4, chat_id = ChatId, label = 0, labeled_by_user_id = (long?)null, labeled_at = "2026-01-02T00:00:00+00:00", reason = "Auto-banned", audit_log_id = 77L }),
                Row(new { message_id = 12, chat_id = ChatId, label = 1, labeled_by_user_id = (long?)null, labeled_at = "2026-01-02T00:00:00+00:00", reason = (string?)null, audit_log_id = (long?)null }),
                Row(new { message_id = 5, chat_id = ChatId, label = 0, labeled_by_user_id = AdminId, labeled_at = "2026-01-06T00:00:00+00:00", reason = (string?)null, audit_log_id = (long?)null })
            ],
            ["image_training_samples"] = [Row(new { id = 1, message_id = 1, chat_id = ChatId })],
            ["video_training_samples"] = []
        }
    };

    private static SystemBackup Migrated()
    {
        var backup = Build();
        Backup30To31VerdictMigration.Apply(backup, NullLogger.Instance);
        return backup;
    }

    private static List<JsonElement> Rows(SystemBackup b) => b.Data!["detection_results"].Cast<JsonElement>().ToList();

    private static JsonElement Get(SystemBackup b, long id) =>
        Rows(b).Single(r => r.GetProperty("id").GetInt64() == id);

    private static int Source(JsonElement r) => r.GetProperty("source").GetInt32();

    private static int Classification(JsonElement r) => r.GetProperty("classification").GetInt32();

    [Test]
    public void Apply_ClassifiesScansWithTheSingleRule()
    {
        var backup = Migrated();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Source(Get(backup, 10)), Is.Zero);
            Assert.That(Classification(Get(backup, 10)), Is.EqualTo(3), "below threshold → ImplicitHam");
            Assert.That(Classification(Get(backup, 11)), Is.EqualTo(5), "AI positive below threshold → UntrainedHam");
            Assert.That(Classification(Get(backup, 13)), Is.EqualTo(2), "spam and trained → ImplicitSpam");
            Assert.That(Classification(Get(backup, 14)), Is.EqualTo(3), "AI veto beats the score → ImplicitHam");
            Assert.That(Classification(Get(backup, 15)), Is.EqualTo(4), "single hard block → UntrainedSpam");
            Assert.That(Classification(Get(backup, 16)), Is.EqualTo(3), "per-chat threshold 4.0 beats the global 2.5");
        }
    }

    [Test]
    public void Apply_RecoversDecisionSources()
    {
        var backup = Migrated();

        using (Assert.EnterMultipleScope())
        {
            Assert.That((Source(Get(backup, 12)), Classification(Get(backup, 12))), Is.EqualTo((12, 1)), "WebMarkHam → ExplicitHam");
            Assert.That((Source(Get(backup, 17)), Classification(Get(backup, 17))), Is.EqualTo((1, 4)), "file scan, net > 0 → UntrainedSpam");
            Assert.That((Source(Get(backup, 18)), Classification(Get(backup, 18))), Is.EqualTo((18, 0)), "import label text beats the score sign");
            Assert.That((Source(Get(backup, 19)), Classification(Get(backup, 19))), Is.EqualTo((16, 1)), "chat 0 → TrainingDataPage by score sign");
        }
    }

    [Test]
    public void Apply_StripsLegacyColumnsAndNeverCarriesIsSpam()
    {
        var backup = Migrated();

        foreach (var row in Rows(backup))
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(row.TryGetProperty("net_score", out _), Is.False);
                Assert.That(row.TryGetProperty("used_for_training", out _), Is.False);
                Assert.That(row.TryGetProperty("detection_source", out _), Is.False);
                Assert.That(row.TryGetProperty("is_spam", out _), Is.False, "is_spam is generated from classification");
            }
        }
    }

    [Test]
    public void Apply_BackfillsTrainingExclude_KeepingTheActor()
    {
        var backup = Migrated();

        var exclusion = Rows(backup).Single(r => Source(r) == 17);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exclusion.GetProperty("message_id").GetInt32(), Is.EqualTo(10), "only the unused import row (19 was used for training)");
            Assert.That(Classification(exclusion), Is.EqualTo(4), "spam import → UntrainedSpam");
            Assert.That(exclusion.GetProperty("web_user_id").GetString(), Is.EqualTo("web-admin"));
            Assert.That(exclusion.GetProperty("system_identifier").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(exclusion.GetProperty("detection_method").GetString(), Is.EqualTo("TrainingExclude"));
            Assert.That(exclusion.GetProperty("detected_at").GetDateTimeOffset(),
                Is.EqualTo(DateTimeOffset.Parse("2026-01-04T00:00:00.123457+00:00")), "one microsecond after the source row");
            Assert.That(exclusion.GetProperty("id").GetInt64(), Is.GreaterThan(19));
        }
    }

    [Test]
    public void Apply_FoldsLabels_WithoutDuplicatingMatchedOnes()
    {
        var backup = Migrated();

        var rows = Rows(backup);
        var autoBan = rows.Single(r => r.GetProperty("message_id").GetInt32() == 4);
        var unknownHam = rows.Single(r => r.GetProperty("message_id").GetInt32() == 12);
        var matchedHam = rows.Where(r => r.GetProperty("message_id").GetInt32() == 3).ToList();
        var adminSpam = rows.Where(r => r.GetProperty("message_id").GetInt32() == 5).OrderBy(r => r.GetProperty("id").GetInt64()).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(matchedHam, Has.Count.EqualTo(1), "the ham label matches the WebMarkHam row 12");
            Assert.That(adminSpam, Has.Count.EqualTo(2), "a content scan never matches a label, so the admin spam label is folded");
            Assert.That((Source(adminSpam[1]), Classification(adminSpam[1])), Is.EqualTo((99, 0)));
            Assert.That(adminSpam[1].GetProperty("telegram_user_id").GetInt64(), Is.EqualTo(AdminId));
            Assert.That(adminSpam[1].GetProperty("system_identifier").ValueKind, Is.EqualTo(JsonValueKind.Null));

            Assert.That((Source(autoBan), Classification(autoBan)), Is.EqualTo((10, 0)));
            Assert.That(autoBan.GetProperty("system_identifier").GetString(), Is.EqualTo("auto_detection"));
            Assert.That(autoBan.GetProperty("detection_method").GetString(), Is.EqualTo("AutoBan"));
            Assert.That(autoBan.GetProperty("reason").GetString(), Is.EqualTo("Auto-banned"));
            Assert.That(autoBan.GetProperty("audit_log_id").GetInt64(), Is.EqualTo(77));
            Assert.That(autoBan.GetProperty("id").GetInt64(), Is.GreaterThan(19));

            Assert.That((Source(unknownHam), Classification(unknownHam)), Is.EqualTo((99, 1)));
            Assert.That(unknownHam.GetProperty("system_identifier").GetString(), Is.EqualTo("unknown"));
            Assert.That(unknownHam.GetProperty("reason").GetString(), Is.EqualTo("Migrated training label"));
        }
    }

    [Test]
    public void Apply_GivesEveryRowOneActorAndAUniqueId()
    {
        var rows = Rows(Migrated());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rows.Select(r => r.GetProperty("id").GetInt64()), Is.Unique);
            foreach (var row in rows)
            {
                var actors = new[] { "web_user_id", "telegram_user_id", "system_identifier" }
                    .Count(k => row.TryGetProperty(k, out var v) && v.ValueKind != JsonValueKind.Null);
                Assert.That(actors, Is.EqualTo(1), $"row {row.GetProperty("id")} must have exactly one actor");
            }
        }
    }

    [Test]
    public void Apply_RowsDeserializeIntoTheCurrentDto()
    {
        var rows = Rows(Migrated());

        var dtos = rows.Select(r => JsonSerializer.Deserialize<DetectionResultRecordDto>(r.GetRawText(), RestoreOptions)!).ToList();
        var label = dtos.Single(d => d.MessageId == 4);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dtos, Has.Count.EqualTo(rows.Count));
            Assert.That(label.Source, Is.EqualTo(10));
            Assert.That(label.Classification, Is.Zero);
            Assert.That(label.DetectionMethod, Is.EqualTo("AutoBan"));
            Assert.That(label.Score, Is.EqualTo(5.0));
            Assert.That(label.EditVersion, Is.Zero);
            Assert.That(label.DetectedAt, Is.EqualTo(DateTimeOffset.Parse("2026-01-02T00:00:00+00:00")));
            Assert.That(dtos.Single(d => d.Id == 10).CheckResultsJson, Does.Contain("\"CheckName\": 13"));
        }
    }

    [Test]
    public void Apply_RemovesLegacyTables()
    {
        var backup = Migrated();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(backup.Data!.Keys, Has.None.AnyOf("training_labels", "image_training_samples", "video_training_samples"));
            Assert.That(backup.Metadata.Tables, Has.None.AnyOf("training_labels", "image_training_samples", "video_training_samples"));
            Assert.That(backup.Metadata.TableCount, Is.EqualTo(2));
        }
    }
}
