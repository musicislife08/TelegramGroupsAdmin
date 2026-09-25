using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for ban_celebration_subscribers table.
/// One row per (Telegram user, chat) opt-in to receive that chat's ban celebrations by DM.
/// Deliverability is not stored here — it is the join to telegram_users.bot_dm_enabled.
/// </summary>
[Table("ban_celebration_subscribers")]
public class BanCelebrationSubscriberDto
{
    [Column("telegram_user_id")]
    public long TelegramUserId { get; set; }

    [Column("chat_id")]
    public long ChatId { get; set; }

    [Column("subscribed_at")]
    public DateTimeOffset SubscribedAt { get; set; }

    /// <summary>
    /// Message id of the open "tap to start the bot" prompt in the chat, or null when none is open.
    /// </summary>
    [Column("prompt_message_id")]
    public int? PromptMessageId { get; set; }

    /// <summary>
    /// Quartz job id of the scheduled 60-second prompt deletion, or null when none is pending.
    /// </summary>
    [Column("prompt_delete_job_id")]
    [MaxLength(200)]
    public string? PromptDeleteJobId { get; set; }

    public TelegramUserDto? TelegramUser { get; set; }

    public ManagedChatRecordDto? ManagedChat { get; set; }
}
