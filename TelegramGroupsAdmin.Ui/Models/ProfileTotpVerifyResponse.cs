namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Response for TOTP verification from Profile page.
/// Includes recovery codes on success.
/// </summary>
public record ProfileTotpVerifyResponse : IApiResponse
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    /// <summary>
    /// Recovery codes generated after enabling TOTP (only on success).
    /// </summary>
    public List<string> RecoveryCodes { get; init; } = [];

    public static ProfileTotpVerifyResponse Ok(List<string> recoveryCodes) => new()
    {
        Success = true,
        RecoveryCodes = recoveryCodes
    };

    public static ProfileTotpVerifyResponse Fail(string error) => new() { Success = false, Error = error };
}
