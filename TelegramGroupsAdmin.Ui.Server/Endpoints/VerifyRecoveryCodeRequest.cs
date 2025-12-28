namespace TelegramGroupsAdmin.Ui.Server.Endpoints;

public record VerifyRecoveryCodeRequest(string RecoveryCode, string IntermediateToken);
