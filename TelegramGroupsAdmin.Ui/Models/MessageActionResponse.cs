namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Response for message action operations (delete, spam, ham, etc.).
/// </summary>
public record MessageActionResponse : IApiResponse
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public bool? MessageDeleted { get; init; }
    public int? ChatsAffected { get; init; }
    public bool? TrustRestored { get; init; }

    public static MessageActionResponse Ok(bool? messageDeleted = null, int? chatsAffected = null, bool? trustRestored = null) => new()
    {
        Success = true,
        MessageDeleted = messageDeleted,
        ChatsAffected = chatsAffected,
        TrustRestored = trustRestored
    };

    public static MessageActionResponse Fail(string error) => new() { Success = false, Error = error };
}
