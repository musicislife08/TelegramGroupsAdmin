namespace TelegramGroupsAdmin.Ui.Server.Endpoints;

public record VerifyTotpRequest(string Code, string IntermediateToken);
