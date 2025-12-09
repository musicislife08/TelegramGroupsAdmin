using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for managed_chats table
/// </summary>
[Table("managed_chats")]
public class ManagedChatRecordDto
{
    [Key]
    [Column("chat_id")]
    public long ChatId { get; set; }

    [Column("chat_name")]
    public string? ChatName { get; set; }

    [Column("chat_type")]
    public ManagedChatType ChatType { get; set; }

    [Column("bot_status")]
    public BotChatStatus BotStatus { get; set; }

    [Column("is_admin")]
    public bool IsAdmin { get; set; }

    [Column("added_at")]
    public DateTimeOffset AddedAt { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("last_seen_at")]
    public DateTimeOffset? LastSeenAt { get; set; }

    [Column("settings_json")]
    public string? SettingsJson { get; set; }

    [Column("chat_icon_path")]
    public string? ChatIconPath { get; set; }

    // Navigation properties
    public virtual ICollection<ChatAdminRecordDto> ChatAdmins { get; set; } = [];

    /// <summary>
    /// Linked channel for this chat (1:1 relationship - groups can only have one linked channel)
    /// </summary>
    public virtual LinkedChannelRecordDto? LinkedChannel { get; set; }
}
