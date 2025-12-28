namespace TelegramGroupsAdmin.Ui.Models;

public record BackupMetadataRequest(string BackupBase64, string? Passphrase = null);
