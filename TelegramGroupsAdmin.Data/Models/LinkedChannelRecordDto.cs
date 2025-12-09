using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for linked_channels table.
/// Stores linked channel info for managed chats (1:1 relationship).
/// Used for impersonation detection against channel names/photos.
/// </summary>
[Table("linked_channels")]
public class LinkedChannelRecordDto
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    /// <summary>
    /// FK to managed_chats - the group this channel is linked to
    /// </summary>
    [Column("managed_chat_id")]
    public long ManagedChatId { get; set; }

    /// <summary>
    /// Telegram channel ID
    /// </summary>
    [Column("channel_id")]
    public long ChannelId { get; set; }

    /// <summary>
    /// Channel display name (Title)
    /// </summary>
    [Column("channel_name")]
    public string? ChannelName { get; set; }

    /// <summary>
    /// Relative path to downloaded channel icon (from wwwroot/images)
    /// </summary>
    [Column("channel_icon_path")]
    public string? ChannelIconPath { get; set; }

    /// <summary>
    /// Perceptual hash (pHash) of channel photo for impersonation comparison.
    /// 8 bytes stored as byte array.
    /// </summary>
    [Column("photo_hash")]
    public byte[]? PhotoHash { get; set; }

    /// <summary>
    /// When this record was last synced with Telegram API
    /// </summary>
    [Column("last_synced")]
    public DateTimeOffset LastSynced { get; set; }

    // Navigation property
    [ForeignKey(nameof(ManagedChatId))]
    public virtual ManagedChatRecordDto ManagedChat { get; set; } = null!;
}
