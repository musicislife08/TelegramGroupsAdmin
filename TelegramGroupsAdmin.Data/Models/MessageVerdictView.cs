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
}
