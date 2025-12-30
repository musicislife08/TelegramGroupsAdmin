namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Response containing the VAPID public key for WebPush subscription.
/// </summary>
public record VapidKeyResponse : IApiResponse
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    /// <summary>
    /// The VAPID public key for WebPush subscription.
    /// </summary>
    public string? VapidPublicKey { get; init; }

    public static VapidKeyResponse Ok(string vapidPublicKey) => new()
    {
        Success = true,
        VapidPublicKey = vapidPublicKey
    };

    public static VapidKeyResponse Fail(string error) => new() { Success = false, Error = error };
}
