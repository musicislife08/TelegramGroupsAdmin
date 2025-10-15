namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Verification token for UI display
/// </summary>
public record VerificationToken(
    long Id,
    string UserId,
    TokenType TokenType,
    string Token,
    string? Value,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UsedAt
)
{
    public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;
    public bool IsUsed => UsedAt.HasValue;
    public bool IsValid => !IsExpired && !IsUsed;
}

public enum TokenType
{
    EmailVerification = 0,
    PasswordReset = 1,
    EmailChange = 2
}
