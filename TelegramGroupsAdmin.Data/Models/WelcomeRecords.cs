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
    /// Response type: Pending, Accepted, Denied, Timeout, Left
    /// </summary>
    [Column("response")]
    public WelcomeResponseType Response { get; set; } = WelcomeResponseType.Pending;

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
/// User response status to welcome message prompts
/// </summary>
public enum WelcomeResponseType
{
    /// <summary>User has not yet responded to welcome message</summary>
    Pending = 0,

    /// <summary>User accepted the rules and is allowed in chat</summary>
    Accepted = 1,

    /// <summary>User declined the rules and was removed</summary>
    Denied = 2,

    /// <summary>User did not respond within timeout period and was removed</summary>
    Timeout = 3,

    /// <summary>User left the chat before responding</summary>
    Left = 4
}
