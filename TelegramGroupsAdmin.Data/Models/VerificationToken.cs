namespace TelegramGroupsAdmin.Data.Models;

public enum TokenType
{
    EmailVerification,
    PasswordReset,
    EmailChange
}

// DTO for Dapper mapping from SQLite
internal record VerificationTokenDto(
    long id,
    string user_id,
    string token_type,
    string token,
    string? value,
    long expires_at,
    long created_at,
    long? used_at
)
{
    public VerificationToken ToVerificationToken() => new VerificationToken(
        Id: id,
        UserId: user_id,
        TokenType: ParseTokenType(token_type),
        Token: token,
        Value: value,
        ExpiresAt: expires_at,
        CreatedAt: created_at,
        UsedAt: used_at
    );

    private static TokenType ParseTokenType(string tokenType) => tokenType switch
    {
        "email_verify" => TokenType.EmailVerification,
        "password_reset" => TokenType.PasswordReset,
        "email_change" => TokenType.EmailChange,
        _ => throw new ArgumentException($"Unknown token type: {tokenType}")
    };
}

public record VerificationToken(
    long Id,
    string UserId,
    TokenType TokenType,
    string Token,
    string? Value,
    long ExpiresAt,
    long CreatedAt,
    long? UsedAt
)
{
    public bool IsExpired => DateTimeOffset.UtcNow.ToUnixTimeSeconds() > ExpiresAt;
    public bool IsUsed => UsedAt.HasValue;
    public bool IsValid => !IsExpired && !IsUsed;

    public string TokenTypeString => TokenType switch
    {
        TokenType.EmailVerification => "email_verify",
        TokenType.PasswordReset => "password_reset",
        TokenType.EmailChange => "email_change",
        _ => throw new ArgumentException($"Unknown token type: {TokenType}")
    };
}
