namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Response for TOTP setup initiation. Use static factory methods for clarity.
/// </summary>
public record SetupTotpResponse : IApiResponse
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? QrCodeUri { get; init; }
    public string? ManualEntryKey { get; init; }

    public static SetupTotpResponse Ok(string qrCodeUri, string manualEntryKey) => new()
    {
        Success = true,
        QrCodeUri = qrCodeUri,
        ManualEntryKey = manualEntryKey
    };

    public static SetupTotpResponse Fail(string error) => new() { Success = false, Error = error };
}
