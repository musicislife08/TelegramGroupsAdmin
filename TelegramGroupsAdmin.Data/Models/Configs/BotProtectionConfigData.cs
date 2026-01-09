namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of BotProtectionConfig for EF Core JSON column mapping.
/// Maps to business model via ToModel/ToDto extensions.
/// </summary>
public class BotProtectionConfigData
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
    /// List of whitelisted bot usernames
    /// </summary>
    public List<string> WhitelistedBots { get; set; } = [];

    /// <summary>
    /// Log bot join/ban events to audit log
    /// </summary>
    public bool LogBotEvents { get; set; }
}
