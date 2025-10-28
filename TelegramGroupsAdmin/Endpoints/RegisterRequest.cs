namespace TelegramGroupsAdmin.Endpoints;

public record RegisterRequest(string Email, string Password, string? InviteToken);
