namespace TelegramGroupsAdmin.Ui.Server.Endpoints;

public record ResetPasswordRequest(string Token, string NewPassword);
