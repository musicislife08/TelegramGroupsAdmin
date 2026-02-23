using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Domain model for a profile scan result event.
/// Each scan produces one record with full scoring detail.
/// </summary>
public record ProfileScanResultRecord(
    long Id,
    long UserId,
    DateTimeOffset ScannedAt,
    decimal Score,
    ProfileScanOutcome Outcome,
    decimal RuleScore,
    decimal AiScore,
    int? AiConfidence,
    string? AiReason,
    string? AiSignals);
