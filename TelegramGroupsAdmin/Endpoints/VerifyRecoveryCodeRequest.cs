namespace TelegramGroupsAdmin.Endpoints;

public record VerifyRecoveryCodeRequest(string RecoveryCode, string IntermediateToken);
