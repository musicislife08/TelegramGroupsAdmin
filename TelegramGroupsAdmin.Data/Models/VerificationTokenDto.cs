using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for verification_tokens table
/// </summary>
[Table("verification_tokens")]
public class VerificationTokenDto
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("user_id")]
    [Required]
    public string UserId { get; set; } = string.Empty;

    /// <summary>The domain TokenType, stored as its int value.</summary>
    [Column("token_type")]
    public int TokenType { get; set; }

    [Column("token")]
    [Required]
    [MaxLength(256)]
    public string Token { get; set; } = string.Empty;

    [Column("value")]
    public string? Value { get; set; }

    [Column("expires_at")]
    public DateTimeOffset ExpiresAt { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("used_at")]
    public DateTimeOffset? UsedAt { get; set; }

    // Navigation property
    [ForeignKey(nameof(UserId))]
    public virtual UserRecordDto? User { get; set; }

    // Helper properties (not mapped)
    [NotMapped]
    [JsonIgnore]
    public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;

    [NotMapped]
    [JsonIgnore]
    public bool IsUsed => UsedAt.HasValue;

    [NotMapped]
    [JsonIgnore]
    public bool IsValid => !IsExpired && !IsUsed;
}
