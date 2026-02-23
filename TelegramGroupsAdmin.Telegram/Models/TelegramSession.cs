namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Domain model for a WTelegram/MTProto session.
/// Represents a per-admin connection to the Telegram User API.
/// </summary>
public record TelegramSession
{
    public long Id { get; init; }
    public required string WebUserId { get; init; }
    public long? TelegramUserId { get; init; }
    public string? DisplayName { get; init; }
    public string? PhoneNumber { get; init; }
    public byte[] SessionData { get; init; } = [];
    public string? MemberChats { get; init; }
    public bool IsActive { get; init; }
    public DateTimeOffset ConnectedAt { get; init; }
    public DateTimeOffset? LastUsedAt { get; init; }
    public DateTimeOffset? DisconnectedAt { get; init; }
}
