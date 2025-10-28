namespace TelegramGroupsAdmin.Endpoints;

public record VerifyRecoveryCodeRequest(string UserId, string RecoveryCode, string IntermediateToken);
