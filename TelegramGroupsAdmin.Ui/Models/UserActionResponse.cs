namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Response for user action operations (trust, ban, unban, etc.).
/// </summary>
public record UserActionResponse : IApiResponse
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public int? ChatsAffected { get; init; }
    public bool? TrustRestored { get; init; }
    public DateTimeOffset? BannedUntil { get; init; }

    public static UserActionResponse Ok(int? chatsAffected = null, bool? trustRestored = null, DateTimeOffset? bannedUntil = null) => new()
    {
        Success = true,
        ChatsAffected = chatsAffected,
        TrustRestored = trustRestored,
        BannedUntil = bannedUntil
    };

    public static UserActionResponse Fail(string error) => new() { Success = false, Error = error };
}
