namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Response for TOTP setup from Profile page.
/// Returns QR code URI for client-side generation (more compact GIF format).
/// </summary>
public record ProfileTotpSetupResponse : IApiResponse
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    /// <summary>
    /// The otpauth:// URI for QR code generation (client-side via JS).
    /// </summary>
    public string? QrCodeUri { get; init; }

    /// <summary>
    /// Manual entry key for authenticator apps.
    /// </summary>
    public string? ManualEntryKey { get; init; }

    public static ProfileTotpSetupResponse Ok(string qrCodeUri, string manualEntryKey) => new()
    {
        Success = true,
        QrCodeUri = qrCodeUri,
        ManualEntryKey = manualEntryKey
    };

    public static ProfileTotpSetupResponse Fail(string error) => new() { Success = false, Error = error };
}
