namespace TelegramGroupsAdmin.Ui.Server.Endpoints;

public record RegisterRequest(string Email, string Password, string? InviteToken);
