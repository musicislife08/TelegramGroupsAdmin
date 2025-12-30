namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Response for Telegram link token generation.
/// </summary>
public record GenerateLinkTokenResponse : IApiResponse
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    /// <summary>
    /// The generated link token (12 characters).
    /// </summary>
    public string? Token { get; init; }

    /// <summary>
    /// When the token expires (15 minutes from generation).
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    public static GenerateLinkTokenResponse Ok(string token, DateTimeOffset expiresAt) => new()
    {
        Success = true,
        Token = token,
        ExpiresAt = expiresAt
    };

    public static GenerateLinkTokenResponse Fail(string error) => new() { Success = false, Error = error };
}
