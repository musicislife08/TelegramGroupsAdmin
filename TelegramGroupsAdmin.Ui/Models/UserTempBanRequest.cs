namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Request for temporary ban.
/// </summary>
public record UserTempBanRequest(TimeSpan Duration, string? Reason);
