namespace TelegramGroupsAdmin.Ui.Models;

public record BackupRestoreRequest(string BackupBase64, string? Passphrase = null);
