namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Configuration for /invite command
/// Stored in configs.moderation_config JSONB column (ConfigType.Moderation)
/// </summary>
public class InviteCommandConfig
{
    /// <summary>
    /// Enable /invite command globally
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Auto-delete command message (default: true)
    /// </summary>
    public bool DeleteCommandMessage { get; set; } = true;

    /// <summary>
    /// Auto-delete response after N seconds (default: 30)
    /// </summary>
    public int DeleteResponseAfterSeconds { get; set; } = 30;

    /// <summary>
    /// Default configuration
    /// </summary>
    public static InviteCommandConfig Default => new()
    {
        Enabled = true,
        DeleteCommandMessage = true,
        DeleteResponseAfterSeconds = 30
    };
}
