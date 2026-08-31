namespace TelegramGroupsAdmin.Configuration.Services;

/// <summary>
/// Aggregated per-chat presence flags across all chat-overridable config types.
/// Used by admin UI pages that show "this chat has a custom X config" badges to
/// avoid N+1 lookups. Each set contains the chat IDs (excluding the global row)
/// that have a custom config of the corresponding type.
/// </summary>
public sealed record ChatConfigPresenceMap(
    HashSet<long> ContentDetectionChatIds,
    HashSet<long> WelcomeChatIds,
    HashSet<long> ServiceMessageDeletionChatIds,
    HashSet<long> BanCelebrationChatIds);
