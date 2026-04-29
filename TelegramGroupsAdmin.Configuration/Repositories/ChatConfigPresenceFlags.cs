namespace TelegramGroupsAdmin.Configuration.Repositories;

/// <summary>
/// Per-chat presence flags for the multiplexed configs table columns. Used by bulk
/// projection queries to avoid N+1 lookups when a UI page needs to know which chats
/// have any per-chat overrides without loading the full configs.
/// </summary>
public sealed record ChatConfigPresenceFlags(
    bool HasWelcome,
    bool HasServiceMessageDeletion,
    bool HasBanCelebration);
