using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for telegram_sessions table.
/// Stores WTelegram/MTProto session data for per-admin User API connections.
/// Session data is encrypted at rest via Data Protection.
/// </summary>
[Table("telegram_sessions")]
public class TelegramSessionDto
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>
    /// Web user who owns this session (FK to users table)
    /// </summary>
    [Column("web_user_id")]
    public string WebUserId { get; set; } = string.Empty;

    /// <summary>
    /// Navigation property to the web user
    /// </summary>
    [ForeignKey(nameof(WebUserId))]
    public virtual UserRecordDto? User { get; set; }

    /// <summary>
    /// Telegram user ID obtained after successful authentication
    /// </summary>
    [Column("telegram_user_id")]
    public long? TelegramUserId { get; set; }

    /// <summary>
    /// Display name of the connected Telegram account
    /// </summary>
    [Column("display_name")]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Encrypted MTProto session data (Data Protection encrypted bytes).
    /// WTelegram uses this to reconnect without re-authentication.
    /// </summary>
    [Column("session_data")]
    public byte[] SessionData { get; set; } = [];

    /// <summary>
    /// JSONB array of chats/groups this Telegram account is a member of.
    /// Populated on connect via GetAllDialogs(). Format: [{id, title, type}]
    /// Not consumed in Part 1 — future use for send-as-admin and chat dashboard filtering.
    /// </summary>
    [Column("member_chats", TypeName = "jsonb")]
    public string? MemberChats { get; set; }

    /// <summary>
    /// Whether this session is currently active.
    /// Unique partial index enforces one active session per web user.
    /// </summary>
    [Column("is_active")]
    public bool IsActive { get; set; }

    /// <summary>
    /// When this session was first established
    /// </summary>
    [Column("connected_at")]
    public DateTimeOffset ConnectedAt { get; set; }

    /// <summary>
    /// When this session was last used for an API call
    /// </summary>
    [Column("last_used_at")]
    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>
    /// When this session was deactivated (disconnect or revocation)
    /// </summary>
    [Column("disconnected_at")]
    public DateTimeOffset? DisconnectedAt { get; set; }
}
