namespace TelegramGroupsAdmin.Ui.Models;

public record VerifyTotpRequest(string Code, string IntermediateToken);
