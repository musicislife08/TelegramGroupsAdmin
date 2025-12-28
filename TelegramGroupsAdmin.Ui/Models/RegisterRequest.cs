namespace TelegramGroupsAdmin.Ui.Models;

public record RegisterRequest(string Email, string Password, string? InviteToken);
