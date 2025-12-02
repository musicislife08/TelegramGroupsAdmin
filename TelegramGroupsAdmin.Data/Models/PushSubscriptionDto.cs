using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for push_subscriptions table
/// Stores browser push notification subscriptions (Web Push API)
/// </summary>
[Table("push_subscriptions")]
public record PushSubscriptionDto
{
    [Key]
    [Column("id")]
    public long Id { get; init; }

    /// <summary>
    /// User ID (references users table)
    /// </summary>
    [Column("user_id")]
    [MaxLength(450)]
    [Required]
    public string UserId { get; init; } = string.Empty;

    /// <summary>
    /// Push subscription endpoint URL
    /// </summary>
    [Column("endpoint")]
    [Required]
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>
    /// P-256 Diffie-Hellman public key for encryption
    /// </summary>
    [Column("p256dh")]
    [Required]
    public string P256dh { get; init; } = string.Empty;

    /// <summary>
    /// Authentication secret for push message encryption
    /// </summary>
    [Column("auth")]
    [Required]
    public string Auth { get; init; } = string.Empty;

    /// <summary>
    /// User agent string of the browser that created the subscription
    /// </summary>
    [Column("user_agent")]
    public string? UserAgent { get; init; }

    /// <summary>
    /// When the subscription was created
    /// </summary>
    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
