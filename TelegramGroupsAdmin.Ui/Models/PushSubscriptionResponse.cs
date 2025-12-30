namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Response for WebPush subscription operations.
/// </summary>
public record PushSubscriptionResponse : IApiResponse
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    public static PushSubscriptionResponse Ok() => new() { Success = true };
    public static PushSubscriptionResponse Fail(string error) => new() { Success = false, Error = error };
}
