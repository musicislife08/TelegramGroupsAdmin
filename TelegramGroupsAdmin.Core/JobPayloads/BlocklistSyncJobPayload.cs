namespace TelegramGroupsAdmin.Core.JobPayloads;

/// <summary>
/// Payload for BlocklistSyncJob
/// Phase 4.13: URL Filtering
/// </summary>
public record BlocklistSyncJobPayload(
    /// <summary>
    /// Optional: Sync only a specific subscription (null = sync all)
    /// </summary>
    long? SubscriptionId = null,

    /// <summary>
    /// Chat ID for sync (0 = global, non-zero = chat-specific)
    /// </summary>
    long ChatId = 0,

    /// <summary>
    /// Whether to force a full cache rebuild (delete all + re-sync)
    /// </summary>
    bool ForceRebuild = false
);
