using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// Keyless mapping of the message_verdicts view: each message's latest non-FileScan verdict
/// event (detected_at DESC, id DESC), or Unscanned (6) when it has none. The view selects;
/// it applies no spam rules.
/// </summary>
public class MessageVerdictView
{
    [Column("chat_id")]
    public long ChatId { get; set; }

    [Column("message_id")]
    public int MessageId { get; set; }

    [Column("classification")]
    public int Classification { get; set; }

    [Column("is_spam")]
    public bool IsSpam { get; set; }

    [Column("source")]
    public int? Source { get; set; }

    [Column("detected_at")]
    public DateTimeOffset? DetectedAt { get; set; }

    [Column("verdict_id")]
    public long? VerdictId { get; set; }

    public const string CreateViewSql = """
        CREATE VIEW message_verdicts AS
        SELECT m.chat_id, m.message_id,
               COALESCE(v.classification, 6) AS classification,
               COALESCE(v.is_spam, false) AS is_spam,
               v.source, v.detected_at, v.id AS verdict_id
        FROM messages m
        LEFT JOIN LATERAL (
            SELECT d.classification, d.is_spam, d.source, d.detected_at, d.id
            FROM detection_results d
            WHERE d.chat_id = m.chat_id AND d.message_id = m.message_id AND d.source <> 1
            ORDER BY d.detected_at DESC, d.id DESC
            LIMIT 1) v ON true;
        """;

    public const string DropViewSql = "DROP VIEW IF EXISTS message_verdicts";
}
