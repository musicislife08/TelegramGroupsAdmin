namespace TelegramGroupsAdmin.Services;

public record TotpSetupResult(
    string Secret,
    string QrCodeUri,
    string ManualEntryKey
);
