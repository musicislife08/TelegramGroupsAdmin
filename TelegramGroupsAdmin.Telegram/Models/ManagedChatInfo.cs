namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Managed chat with health status information
/// </summary>
public class ManagedChatInfo
{
    public required ManagedChatRecord Record { get; init; }
    public required ChatHealthStatus HealthStatus { get; set; }
    public bool HasCustomSpamConfig { get; set; }
    public bool HasCustomWelcomeConfig { get; set; }
    public bool HasCustomServiceMsgConfig { get; set; }
    public bool HasCustomBanCelebrationConfig { get; set; }
}
