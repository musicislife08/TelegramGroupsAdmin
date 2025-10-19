namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Configuration for bot protection system
/// Blocks unauthorized bots from joining chats
/// Phase 6.1: Bot Auto-Ban
/// Stored in configs table as JSONB
/// </summary>
public class BotProtectionConfig
{
    /// <summary>
    /// Whether bot protection is enabled
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Automatically ban bots unless invited by admin
    /// </summary>
    public bool AutoBanBots { get; set; }

    /// <summary>
    /// Allow bots invited by admins to stay in the chat
    /// </summary>
    public bool AllowAdminInvitedBots { get; set; }

    /// <summary>
    /// List of whitelisted bot usernames (e.g., @RoseBot, @GroupButlerBot)
    /// These bots are always allowed regardless of who invited them
    /// </summary>
    public List<string> WhitelistedBots { get; set; } = new();

    /// <summary>
    /// Log bot join/ban events to audit log
    /// </summary>
    public bool LogBotEvents { get; set; }

    /// <summary>
    /// Default configuration (enabled by default for all chats)
    /// </summary>
    public static BotProtectionConfig Default => new()
    {
        Enabled = true,
        AutoBanBots = true,
        AllowAdminInvitedBots = true,
        WhitelistedBots = new List<string>(),
        LogBotEvents = true
    };
}
