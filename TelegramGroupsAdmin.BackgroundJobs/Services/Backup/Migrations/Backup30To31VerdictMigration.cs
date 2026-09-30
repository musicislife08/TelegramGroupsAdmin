using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup.Migrations;

/// <summary>
/// Backup format 3.0 → 3.1 (single spam verdict): classifies legacy detection_results rows into
/// verdict events (source + classification), backfills TrainingExclude events, folds training_labels
/// into decision rows, strips the retired columns and drops the retired tables, so a 3.0 backup
/// restores into the 3.1 schema.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a separate implementation from the SQL migration <c>AddVerdictEvents</c> (backup-only,
/// time-boxed); its rules mirror that migration. Differences are limited to backup-shape facts:
/// thresholds come from the exported config object, where a per-chat config always carries a
/// ReviewQueueThreshold (the export fills the 2.5 default), so the global fallback only applies to
/// chats without a config row.
/// </para>
/// <para>
/// Remove this class, and its call in <c>BackupService.ApplyBackupMigrations</c>, one year after the
/// release that introduced backup format 3.1.
/// </para>
/// </remarks>
public static class Backup30To31VerdictMigration
{
    private const string DetectionResults = "detection_results";
    private const string TrainingLabels = "training_labels";
    private static readonly string[] RetiredTables = [TrainingLabels, "image_training_samples", "video_training_samples"];
    private static readonly string[] RetiredColumns = ["detection_source", "net_score", "used_for_training", "is_spam"];

    private const double DefaultReviewQueueThreshold = 2.5;

    // VerdictSource values (Core.Models.VerdictSource)
    private const int ContentScan = 0;
    private const int FileScan = 1;
    private const int AutoBan = 10;
    private const int WebMarkSpam = 11;
    private const int WebMarkHam = 12;
    private const int SpamCommand = 13;
    private const int ReviewSpam = 14;
    private const int TrainingDataPage = 16;
    private const int TrainingExclude = 17;
    private const int Import = 18;
    private const int LegacyManual = 99;

    // VerdictClassification values (Core.Models.VerdictClassification)
    private const int ExplicitSpam = 0;
    private const int ExplicitHam = 1;
    private const int ImplicitSpam = 2;
    private const int ImplicitHam = 3;
    private const int UntrainedSpam = 4;
    private const int UntrainedHam = 5;

    // CheckName values (ContentDetection CheckName)
    private const int OpenAiCheck = 6;
    private const int UrlBlocklistCheck = 8;

    // Detail prefixes of an OpenAI clean answer (the veto), across its wording history.
    private static readonly string[] OpenAICleanAnswerPrefixes = ["OpenAI vetoed spam", "OpenAI: Clean -", "AI: Clean -"];

    public static void Apply(SystemBackup backup, ILogger logger)
    {
        if (backup.Data is null)
        {
            RemoveRetiredTables(backup);
            return;
        }

        var thresholds = ReadReviewThresholds(backup.Data);
        var rows = backup.Data.TryGetValue(DetectionResults, out var detectionRows)
            ? detectionRows.Select(ToObject).ToList()
            : [];

        // 1–2. source and classification for every legacy row
        foreach (var row in rows)
        {
            var source = SourceOf(row);
            var repairedVeto = source == ContentScan && RepairLegacyVeto(row);
            var classification = ClassificationOf(row, source, thresholds);
            row["source"] = source;
            row["classification"] = classification;
            if (source == ContentScan)
                row["properties"] = repairedVeto
                    ? """{"backfilled": true, "repaired_legacy_veto": true}"""
                    : """{"backfilled": true}""";
        }

        var nextId = rows.Count == 0 ? 1 : rows.Max(r => Long(r["id"]) ?? 0) + 1;
        var added = new List<JsonObject>();

        // 3. past exclusions: chat-0 page / import rows an admin removed from training
        foreach (var excluded in rows.Where(r => Int(r["source"]) is TrainingDataPage or Import && Bool(r["used_for_training"]) == false))
        {
            added.Add(new JsonObject
            {
                ["id"] = nextId++,
                ["message_id"] = excluded["message_id"]?.DeepClone(),
                ["chat_id"] = excluded["chat_id"]?.DeepClone(),
                ["detected_at"] = ParseTimestamp(excluded["detected_at"]).AddTicks(10).ToString("O", CultureInfo.InvariantCulture),
                ["detection_method"] = "TrainingExclude",
                ["score"] = excluded["score"]?.DeepClone() ?? 0.0,
                ["reason"] = "Removed from training (backfilled from used_for_training = false)",
                ["web_user_id"] = excluded["web_user_id"]?.DeepClone(),
                ["telegram_user_id"] = excluded["telegram_user_id"]?.DeepClone(),
                ["system_identifier"] = excluded["system_identifier"]?.DeepClone(),
                ["check_results_json"] = null,
                ["edit_version"] = 0,
                ["source"] = TrainingExclude,
                ["classification"] = Int(excluded["classification"]) == ExplicitSpam ? UntrainedSpam : UntrainedHam,
                ["properties"] = """{"backfilled": true, "from": "used_for_training"}""",
                ["audit_log_id"] = null
            });
        }

        // 4. fold training_labels into decision events, unless a decision of the same polarity exists
        var foldedLabels = 0;
        if (backup.Data.TryGetValue(TrainingLabels, out var labels))
        {
            // Explicit decisions already in the log, keyed once (not scanned per label).
            var explicitDecisions = rows
                .Where(r => Int(r["source"]) is not (ContentScan or FileScan or TrainingExclude)
                    && Int(r["classification"]) is ExplicitSpam or ExplicitHam)
                .Select(r => (ChatId: Long(r["chat_id"]), MessageId: Long(r["message_id"]), IsSpam: Int(r["classification"]) == ExplicitSpam))
                .ToHashSet();

            foreach (var label in labels.Select(ToObject))
            {
                var messageId = Long(label["message_id"]);
                var chatId = Long(label["chat_id"]);
                var isSpamLabel = Int(label["label"]) == 0;
                var labeledBy = Long(label["labeled_by_user_id"]);

                if (explicitDecisions.Contains((chatId, messageId, isSpamLabel)))
                    continue;

                var autoBan = isSpamLabel && labeledBy is null;
                added.Add(new JsonObject
                {
                    ["id"] = nextId++,
                    ["message_id"] = label["message_id"]?.DeepClone(),
                    ["chat_id"] = label["chat_id"]?.DeepClone(),
                    ["detected_at"] = label["labeled_at"]?.DeepClone(),
                    ["detection_method"] = autoBan ? "AutoBan" : "Manual",
                    ["score"] = 5.0,
                    ["reason"] = Str(label["reason"]) ?? "Migrated training label",
                    ["web_user_id"] = null,
                    ["telegram_user_id"] = labeledBy,
                    ["system_identifier"] = labeledBy is null ? (isSpamLabel ? "auto_detection" : "unknown") : null,
                    ["check_results_json"] = null,
                    ["edit_version"] = 0,
                    ["source"] = autoBan ? AutoBan : LegacyManual,
                    ["classification"] = isSpamLabel ? ExplicitSpam : ExplicitHam,
                    ["properties"] = """{"backfilled": true, "from": "training_labels"}""",
                    ["audit_log_id"] = label["audit_log_id"]?.DeepClone()
                });
                foldedLabels++;
            }
        }

        foreach (var row in rows)
        {
            foreach (var column in RetiredColumns)
                row.Remove(column);
        }

        if (backup.Data.ContainsKey(DetectionResults) || added.Count > 0)
            backup.Data[DetectionResults] = [.. rows.Concat(added).Select(o => (object)JsonSerializer.SerializeToElement(o))];

        RemoveRetiredTables(backup);

        logger.LogInformation(
            "Backup 3.0→3.1: classified {Rows} detection rows, backfilled {Exclusions} training exclusions, folded {Labels} training labels",
            rows.Count, added.Count - foldedLabels, foldedLabels);
    }

    private static void RemoveRetiredTables(SystemBackup backup)
    {
        foreach (var table in RetiredTables)
        {
            backup.Data?.Remove(table);
            if (backup.Metadata.Tables.Remove(table))
                backup.Metadata.TableCount--;
        }
    }

    private static int SourceOf(JsonObject row)
    {
        var detectionSource = Str(row["detection_source"]) ?? "";
        var reason = Str(row["reason"]) ?? "";
        return detectionSource switch
        {
            "auto" or "auto_detection" => ContentScan,
            "file_scan" => FileScan,
            "tg-spam-import" => Import,
            "manual" => ManualSourceOf(row, reason),
            // Every value the app ever wrote is listed above; anything else would silently become an
            // explicit training decision, so refuse the backup instead (matches the AddVerdictEvents guard).
            _ => throw new InvalidOperationException(
                $"Backup detection_results row {Long(row["id"])} has unknown detection_source '{detectionSource}'")
        };
    }

    private static int ManualSourceOf(JsonObject row, string reason)
    {
        return reason switch
        {
            _ when Long(row["chat_id"]) == 0 => TrainingDataPage,
            _ when reason.StartsWith("Manually marked as spam by admin via UI", StringComparison.Ordinal) => WebMarkSpam,
            _ when reason.StartsWith("Manually marked as ham", StringComparison.Ordinal)
                || reason.StartsWith("Manually added as ham training sample", StringComparison.Ordinal) => WebMarkHam,
            _ when reason.StartsWith("Spam detected via /spam", StringComparison.Ordinal) => SpamCommand,
            _ when reason.StartsWith("Report #", StringComparison.Ordinal) => ReviewSpam,
            _ => LegacyManual
        };
    }

    private static int ClassificationOf(JsonObject row, int source, Dictionary<long, double> thresholds)
    {
        var netScore = Double(row["net_score"]) ?? 0;
        switch (source)
        {
            case FileScan:
                return netScore > 0 ? UntrainedSpam : UntrainedHam;
            case Import:
                var reason = Str(row["reason"]) ?? "";
                return reason.Contains("label - ham", StringComparison.OrdinalIgnoreCase) ? ExplicitHam
                    : reason.Contains("label - spam", StringComparison.OrdinalIgnoreCase) ? ExplicitSpam
                    : netScore > 0 ? ExplicitSpam : ExplicitHam;
            case AutoBan or WebMarkSpam or SpamCommand or ReviewSpam:
                return ExplicitSpam;
            case WebMarkHam:
                return ExplicitHam;
            case ContentScan:
                return ContentScanClassificationOf(row, netScore, thresholds);
            default:
                return netScore > 0 ? ExplicitSpam : ExplicitHam;
        }
    }

    /// <summary>Content scan: the one rule (score >= ReviewQueueThreshold), AI veto, single hard block.</summary>
    private static int ContentScanClassificationOf(JsonObject row, double netScore, Dictionary<long, double> thresholds)
    {
        var checks = ParseChecks(row["check_results_json"]);
        var ai = checks.FirstOrDefault(c => c.CheckName == OpenAiCheck);
        var aiVeto = ai is { Abstained: false, Score: 0 };
        var aiPositive = ai is { Abstained: false, Score: > 0 };
        var hardBlock = checks.Count == 1 && checks[0] is { CheckName: UrlBlocklistCheck, Score: >= 5 };
        var chatId = Long(row["chat_id"]) ?? 0;
        var threshold = thresholds.TryGetValue(chatId, out var chatThreshold) ? chatThreshold
            : thresholds.GetValueOrDefault(0, DefaultReviewQueueThreshold);
        var isSpam = !aiVeto && (hardBlock || netScore >= threshold);

        if (isSpam)
            return Bool(row["used_for_training"]) == true ? ImplicitSpam : UntrainedSpam;
        return aiPositive ? UntrainedHam : ImplicitHam;
    }

    /// <summary>
    /// RemoveV1ContentDetectionBridge (2026-03-06) converted V1 "clean" check results to Abstained=true with
    /// Score = Confidence / 20; for OpenAI that "clean" was an answer (the veto). Rewrites those checks,
    /// recognised by the answer text, to the veto encoding (Abstained=false, Score=0), in the representation
    /// the row came in. Mirrors the AddVerdictEvents repair. Returns whether the row changed.
    /// </summary>
    private static bool RepairLegacyVeto(JsonObject row)
    {
        var root = ParseCheckResults(row["check_results_json"], out var fromText);
        if (root?["Checks"] is not JsonArray checks)
            return false;

        var repaired = false;
        foreach (var check in checks.OfType<JsonObject>())
        {
            if (Int(check["CheckName"]) == OpenAiCheck
                && Bool(check["Abstained"]) == true
                && IsOpenAICleanAnswer(Str(check["Details"])))
            {
                check["Abstained"] = false;
                check["Score"] = 0;
                repaired = true;
            }
        }

        if (repaired && fromText)
            row["check_results_json"] = root.ToJsonString();
        return repaired;
    }

    private static bool IsOpenAICleanAnswer(string? details) =>
        details is not null && OpenAICleanAnswerPrefixes.Any(p => details.StartsWith(p, StringComparison.Ordinal));

    /// <summary>A check result. Abstained / Score are null when absent (the SQL treats those as neither veto nor positive).</summary>
    private sealed record Check(int? CheckName, double? Score, bool? Abstained);

    private static List<Check> ParseChecks(JsonNode? node)
    {
        if (ParseCheckResults(node, out _)?["Checks"] is not JsonArray checks)
            return [];

        return [.. checks.OfType<JsonObject>().Select(c => new Check(Int(c["CheckName"]), Double(c["Score"]), Bool(c["Abstained"])))];
    }

    /// <summary>
    /// The check_results_json object, or null when there is none or it is not a JSON object (malformed
    /// text included): such a row is read as having no checks, as the SQL migration reads a non-array
    /// "Checks", instead of failing the whole restore. check_results_json is a string DTO property, so
    /// the export writes the raw jsonb text; <paramref name="fromText"/> reports that shape.
    /// </summary>
    private static JsonObject? ParseCheckResults(JsonNode? node, out bool fromText)
    {
        fromText = false;
        switch (node)
        {
            case JsonObject o:
                return o;
            case JsonValue v when v.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text):
                fromText = true;
                try
                {
                    return JsonNode.Parse(text) as JsonObject;
                }
                catch (JsonException)
                {
                    return null;
                }
            default:
                return null;
        }
    }

    /// <summary>
    /// Per-chat ReviewQueueThreshold. content_detection_configs.Config is a typed DTO property, so the
    /// export embeds it as an object with snake_case keys under "config".
    /// </summary>
    private static Dictionary<long, double> ReadReviewThresholds(Dictionary<string, List<object>> data)
    {
        var result = new Dictionary<long, double>();
        if (!data.TryGetValue("content_detection_configs", out var configs))
            return result;

        foreach (var config in configs.Select(ToObject))
        {
            var chatId = Long(config["chat_id"]) ?? 0; // SCHEMA-3 already turned a NULL global into 0
            var threshold = Double((config["config"] as JsonObject)?["review_queue_threshold"]);
            if (threshold is not null)
                result[chatId] = threshold.Value;
        }

        return result;
    }

    private static JsonObject ToObject(object row) => row switch
    {
        JsonElement element => JsonNode.Parse(element.GetRawText())!.AsObject(),
        _ => JsonSerializer.SerializeToNode(row)!.AsObject()
    };

    private static DateTimeOffset ParseTimestamp(JsonNode? node) =>
        DateTimeOffset.Parse(Str(node) ?? throw new InvalidOperationException("detection_results row without detected_at"),
            CultureInfo.InvariantCulture);

    private static string? Str(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static long? Long(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<long>(out var l) ? l : null;

    private static int? Int(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<int>(out var i) ? i : null;

    private static double? Double(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;

    private static bool? Bool(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;
}
