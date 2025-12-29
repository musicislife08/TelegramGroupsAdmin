namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Request to permanently ban a user from all managed chats.
/// </summary>
public record UserBanRequest(string? Reason);
