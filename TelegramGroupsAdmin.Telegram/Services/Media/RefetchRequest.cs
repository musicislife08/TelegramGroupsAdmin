using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Media;

/// <summary>
/// Request to refetch media or user photo from Telegram
/// Used by MediaRefetchQueueService to queue downloads
/// </summary>
public record RefetchRequest
{
    /// <summary>Message ID (for media refetch)</summary>
    public long MessageId { get; init; }

    /// <summary>Type of media to refetch (null for user photos)</summary>
    public MediaType? MediaType { get; init; }

    /// <summary>Type of refetch operation</summary>
    public RefetchType Type { get; init; }

    /// <summary>User ID (for user photo refetch)</summary>
    public long? UserId { get; init; }

    /// <summary>
    /// Generate unique key for deduplication
    /// </summary>
    public string GetKey() => Type == RefetchType.Media
        ? $"{MessageId}:{MediaType}"
        : $"{UserId}:photo";
}
