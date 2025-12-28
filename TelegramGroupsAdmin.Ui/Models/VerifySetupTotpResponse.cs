namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Response for TOTP setup verification. Use static factory methods for clarity.
/// </summary>
public record VerifySetupTotpResponse : IApiResponse
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<string>? RecoveryCodes { get; init; }

    public static VerifySetupTotpResponse Ok(IReadOnlyList<string> recoveryCodes) => new()
    {
        Success = true,
        RecoveryCodes = recoveryCodes
    };

    public static VerifySetupTotpResponse Fail(string error) => new() { Success = false, Error = error };
}
