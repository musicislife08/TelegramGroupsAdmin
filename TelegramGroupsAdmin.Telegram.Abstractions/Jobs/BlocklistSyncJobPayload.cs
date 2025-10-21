namespace TelegramGroupsAdmin.Telegram.Abstractions.Jobs;

/// <summary>
/// Payload for BlocklistSyncJob
/// Phase 4.13: URL Filtering
/// </summary>
public class BlocklistSyncJobPayload
{
    /// <summary>
    /// Optional: Sync only a specific subscription (null = sync all)
    /// </summary>
    public long? SubscriptionId { get; set; }

    /// <summary>
    /// Chat ID for sync (0 = global, non-zero = chat-specific)
    /// </summary>
    public long ChatId { get; set; } = 0;

    /// <summary>
    /// Whether to force a full cache rebuild (delete all + re-sync)
    /// </summary>
    public bool ForceRebuild { get; set; }
}
