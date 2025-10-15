using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

public enum TokenType
{
    EmailVerification,
    PasswordReset,
    EmailChange
}

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

    [Column("token_type")]
    [Required]
    [MaxLength(50)]
    public string TokenTypeString { get; set; } = string.Empty;

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
    public TokenType TokenType
    {
        get => ParseTokenType(TokenTypeString);
        set => TokenTypeString = value switch
        {
            TokenType.EmailVerification => "email_verify",
            TokenType.PasswordReset => "password_reset",
            TokenType.EmailChange => "email_change",
            _ => throw new ArgumentException($"Unknown token type: {value}")
        };
    }

    [NotMapped]
    public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;

    [NotMapped]
    public bool IsUsed => UsedAt.HasValue;

    [NotMapped]
    public bool IsValid => !IsExpired && !IsUsed;

    private static TokenType ParseTokenType(string tokenType) => tokenType switch
    {
        "email_verify" => TokenType.EmailVerification,
        "password_reset" => TokenType.PasswordReset,
        "email_change" => TokenType.EmailChange,
        _ => throw new ArgumentException($"Unknown token type: {tokenType}")
    };
}
