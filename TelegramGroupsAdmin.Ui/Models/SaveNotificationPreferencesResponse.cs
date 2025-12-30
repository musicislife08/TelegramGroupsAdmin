namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Response for saving notification preferences.
/// </summary>
public record SaveNotificationPreferencesResponse : IApiResponse
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    public static SaveNotificationPreferencesResponse Ok() => new() { Success = true };
    public static SaveNotificationPreferencesResponse Fail(string error) => new() { Success = false, Error = error };
}
