namespace TelegramGroupsAdmin.Configuration;

/// <summary>
/// Configuration for ban celebration feature.
/// Controls whether and how celebratory GIFs are posted when users are banned.
/// Stored in configs table as JSONB.
/// Supports global defaults (chat_id=0) and per-chat overrides.
/// </summary>
public class BanCelebrationConfig
{
    /// <summary>
    /// Master toggle to enable/disable ban celebrations for this chat.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Send celebration GIF when automatic spam detection bans a user.
    /// </summary>
    public bool TriggerOnAutoBan { get; set; } = true;

    /// <summary>
    /// Send celebration GIF when an admin manually bans a user.
    /// </summary>
    public bool TriggerOnManualBan { get; set; } = true;

    /// <summary>
    /// Send the celebration GIF via DM to the banned user (maximum personal impact).
    /// Requires DM-based welcome mode to be enabled for the chat.
    /// </summary>
    public bool SendToBannedUser { get; set; } = true;

    /// <summary>
    /// Default configuration - feature disabled, but all triggers enabled if activated.
    /// </summary>
    public static BanCelebrationConfig Default => new();
}
