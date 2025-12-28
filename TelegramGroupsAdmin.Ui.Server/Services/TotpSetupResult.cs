namespace TelegramGroupsAdmin.Ui.Server.Services;

public record TotpSetupResult(
    string Secret,
    string QrCodeUri,
    string ManualEntryKey
);
