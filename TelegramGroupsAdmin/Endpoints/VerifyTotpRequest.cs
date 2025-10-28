namespace TelegramGroupsAdmin.Endpoints;

public record VerifyTotpRequest(string UserId, string Code, string IntermediateToken);
