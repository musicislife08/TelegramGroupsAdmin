namespace TelegramGroupsAdmin.Endpoints;

public record VerifyTotpRequest(string Code, string IntermediateToken);
