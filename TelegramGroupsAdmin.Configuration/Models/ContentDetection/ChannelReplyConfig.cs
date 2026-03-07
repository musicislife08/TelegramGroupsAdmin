namespace TelegramGroupsAdmin.Configuration.Models.ContentDetection;

/// <summary>
/// Configuration for channel reply spam signal detection.
/// Adds a confidence boost when a message replies to a channel post
/// (linked channel system post or anonymous admin posting as the group).
/// </summary>
public class ChannelReplyConfig
{
    /// <summary>
    /// Whether to use global configuration instead of chat-specific overrides
    /// </summary>
    public bool UseGlobal { get; set; } = true;

    /// <summary>
    /// Whether channel reply detection is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Always run this check for all users (bypasses trust/admin status)
    /// </summary>
    public bool AlwaysRun { get; set; }
}
