using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// Bot membership status in a Telegram chat (stored as INT in database)
/// </summary>
public enum BotChatStatus
{
    /// <summary>Bot is a regular member without admin privileges</summary>
    Member = 0,
    /// <summary>Bot is an administrator with elevated permissions</summary>
    Administrator = 1,
    /// <summary>Bot left the chat voluntarily</summary>
    Left = 2,
    /// <summary>Bot was kicked/removed from the chat</summary>
    Kicked = 3
}

/// <summary>
/// Telegram chat type classification (stored as INT in database)
/// </summary>
public enum ManagedChatType
{
    /// <summary>Private one-on-one chat</summary>
    Private = 0,
    /// <summary>Basic group chat (legacy, up to 200 members)</summary>
    Group = 1,
    /// <summary>Supergroup chat (modern, unlimited members)</summary>
    Supergroup = 2,
    /// <summary>Broadcast channel</summary>
    Channel = 3
}

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
}
