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

/// <summary>
/// Types of verification tokens for user authentication flows
/// </summary>
public enum TokenType
{
    /// <summary>Token for verifying email address during registration</summary>
    EmailVerification = 0,

    /// <summary>Token for resetting forgotten password</summary>
    PasswordReset = 1,

    /// <summary>Token for confirming new email address change</summary>
    EmailChange = 2
}
