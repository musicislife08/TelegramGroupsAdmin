namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Health status for a chat (health-related info only, no chat metadata)
/// </summary>
public class ChatHealthStatus
{
    public long ChatId { get; set; }
    public bool IsReachable { get; set; }
    public string BotStatus { get; set; } = "Unknown";
    public bool IsAdmin { get; set; }
    public bool CanDeleteMessages { get; set; }
    public bool CanRestrictMembers { get; set; }
    public bool CanPromoteMembers { get; set; }
    public bool CanInviteUsers { get; set; }
    public int AdminCount { get; set; }
    public string Status { get; set; } = "Unknown";
    public List<string> Warnings { get; set; } = new();
}
