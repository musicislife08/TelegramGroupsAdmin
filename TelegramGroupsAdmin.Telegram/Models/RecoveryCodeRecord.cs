namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Recovery code record for UI display
/// </summary>
public record RecoveryCodeRecord(
    long Id,
    string UserId,
    string CodeHash,
    DateTimeOffset? UsedAt
);
