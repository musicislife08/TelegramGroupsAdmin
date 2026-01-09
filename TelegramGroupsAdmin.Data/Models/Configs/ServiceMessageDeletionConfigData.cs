namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of ServiceMessageDeletionConfig for EF Core JSON column mapping.
/// Maps to business model via ToModel/ToDto extensions.
/// </summary>
public class ServiceMessageDeletionConfigData
{
    /// <summary>
    /// Delete "X joined the group" messages
    /// </summary>
    public bool DeleteJoinMessages { get; set; } = true;

    /// <summary>
    /// Delete "X left the group" messages
    /// </summary>
    public bool DeleteLeaveMessages { get; set; } = true;

    /// <summary>
    /// Delete photo change messages
    /// </summary>
    public bool DeletePhotoChanges { get; set; } = true;

    /// <summary>
    /// Delete "X changed the group title" messages
    /// </summary>
    public bool DeleteTitleChanges { get; set; } = true;

    /// <summary>
    /// Delete "X pinned a message" notifications
    /// </summary>
    public bool DeletePinNotifications { get; set; } = true;

    /// <summary>
    /// Delete chat creation messages
    /// </summary>
    public bool DeleteChatCreationMessages { get; set; } = true;
}
