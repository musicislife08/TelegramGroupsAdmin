namespace TelegramGroupsAdmin.Configuration;

/// <summary>
/// Configuration for service message deletion.
/// Controls which types of Telegram service messages are automatically deleted.
/// Stored in configs table as JSONB.
/// Supports global defaults (chat_id=0) and per-chat overrides.
/// </summary>
public class ServiceMessageDeletionConfig
{
    /// <summary>
    /// Delete "X joined the group" messages (NewChatMembers).
    /// </summary>
    public bool DeleteJoinMessages { get; set; } = true;

    /// <summary>
    /// Delete "X left the group" messages (LeftChatMember).
    /// </summary>
    public bool DeleteLeaveMessages { get; set; } = true;

    /// <summary>
    /// Delete photo change messages (NewChatPhoto, DeleteChatPhoto).
    /// </summary>
    public bool DeletePhotoChanges { get; set; } = true;

    /// <summary>
    /// Delete "X changed the group title" messages (NewChatTitle).
    /// </summary>
    public bool DeleteTitleChanges { get; set; } = true;

    /// <summary>
    /// Delete "X pinned a message" notifications (PinnedMessage).
    /// </summary>
    public bool DeletePinNotifications { get; set; } = true;

    /// <summary>
    /// Delete chat creation messages (GroupChatCreated, SupergroupChatCreated, ChannelChatCreated).
    /// </summary>
    public bool DeleteChatCreationMessages { get; set; } = true;

    /// <summary>
    /// Default configuration - all service messages deleted (preserves current behavior).
    /// </summary>
    public static ServiceMessageDeletionConfig Default => new();
}
