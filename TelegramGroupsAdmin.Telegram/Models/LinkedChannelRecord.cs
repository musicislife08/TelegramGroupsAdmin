namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Linked channel record for UI display.
/// Represents a channel linked to a managed group chat for impersonation detection.
/// </summary>
public record LinkedChannelRecord(
    int Id,
    long ManagedChatId,
    long ChannelId,
    string? ChannelName,
    string? ChannelIconPath,
    byte[]? PhotoHash,
    DateTimeOffset LastSynced
);
