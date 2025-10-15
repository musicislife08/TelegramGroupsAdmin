using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for welcome_responses table
/// Tracks user responses to welcome messages (accept, deny, timeout, left)
/// Phase 4.4: Welcome Message System
/// </summary>
[Table("welcome_responses")]
public class WelcomeResponseDto
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public long Id { get; set; }

    [Column("chat_id")]
    public long ChatId { get; set; }

    [Column("user_id")]
    public long UserId { get; set; }

    [Column("username")]
    public string? Username { get; set; }

    [Column("welcome_message_id")]
    public int WelcomeMessageId { get; set; }

    /// <summary>
    /// Response type: 'pending', 'accepted', 'denied', 'timeout', 'left'
    /// </summary>
    [Column("response")]
    [MaxLength(20)]
    public string Response { get; set; } = string.Empty;

    [Column("responded_at")]
    public DateTimeOffset RespondedAt { get; set; }

    /// <summary>
    /// Did we successfully send rules via DM?
    /// </summary>
    [Column("dm_sent")]
    public bool DmSent { get; set; }

    /// <summary>
    /// Did we fall back to posting rules in chat because DM failed?
    /// </summary>
    [Column("dm_fallback")]
    public bool DmFallback { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// TickerQ job ID for the timeout job (null if job completed/cancelled)
    /// </summary>
    [Column("timeout_job_id")]
    public Guid? TimeoutJobId { get; set; }
}

/// <summary>
/// Response types for welcome messages
/// </summary>
public enum WelcomeResponseType
{
    Pending = 0,
    Accepted = 1,
    Denied = 2,
    Timeout = 3,
    Left = 4
}
