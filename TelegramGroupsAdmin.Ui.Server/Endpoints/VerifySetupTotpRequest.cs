namespace TelegramGroupsAdmin.Ui.Server.Endpoints;

public record VerifySetupTotpRequest(string Code, string IntermediateToken);
