namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Bot permissions test result
/// </summary>
public class BotPermissionsTest
{
    public long ChatId { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string BotStatus { get; set; } = "Unknown";
    public bool IsAdmin { get; set; }
    public bool CanDeleteMessages { get; set; }
    public bool CanRestrictMembers { get; set; }
    public bool CanPromoteMembers { get; set; }
    public bool CanInviteUsers { get; set; }
    public bool CanPinMessages { get; set; }
}
