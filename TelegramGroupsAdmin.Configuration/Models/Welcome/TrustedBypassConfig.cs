namespace TelegramGroupsAdmin.Configuration.Models.Welcome;

/// <summary>
/// Configuration for the trusted user / privileged bypass feature of the welcome system.
/// Stored inside the Welcome JSONB config row under <see cref="WelcomeConfig.TrustedBypass"/>.
/// </summary>
public class TrustedBypassConfig
{
    // Public so UI helper text and service code reference the same token.
    public const string UsernameVariable = "{username}";
    public const string ChatNameVariable = "{chat_name}";

    // Internal so tests, UI reset-to-default, and .Default factories share one source of truth.
    internal const string DefaultAnnouncementMessage =
        UsernameVariable + " welcomed automatically — trusted from other groups.";
    internal const int DefaultAnnouncementTtlSeconds = 30;

    /// <summary>
    /// Master toggle for the trusted-user bypass.
    /// When true, users with <c>IsTrusted = true</c> skip the welcome consent flow and all security checks.
    /// Web admins (GlobalAdmin/Owner with linked TelegramUserId) and Telegram chat admins
    /// always bypass regardless of this flag.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Message posted in chat when a bypass occurs.
    /// Variables: <see cref="UsernameVariable"/>, <see cref="ChatNameVariable"/>.
    /// </summary>
    public string AnnouncementMessage { get; set; } = DefaultAnnouncementMessage;

    /// <summary>
    /// Seconds until the announcement is auto-deleted. Range: 10-300.
    /// </summary>
    public int AnnouncementTtlSeconds { get; set; } = DefaultAnnouncementTtlSeconds;
}
