namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// A message's current verdict, as resolved by the message_verdicts view (latest event wins).
/// Source/DetectedAt/VerdictId are null when the message is Unscanned.
/// </summary>
public sealed record MessageVerdict(
    long ChatId,
    int MessageId,
    VerdictClassification Classification,
    bool IsSpam,
    VerdictSource? Source,
    DateTimeOffset? DetectedAt,
    long? VerdictId);
