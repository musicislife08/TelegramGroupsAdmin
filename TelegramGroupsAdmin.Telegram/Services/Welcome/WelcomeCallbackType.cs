namespace TelegramGroupsAdmin.Telegram.Services.Welcome;

/// <summary>
/// Callback type for welcome system button clicks.
/// </summary>
public enum WelcomeCallbackType
{
    /// <summary>User accepted rules in chat</summary>
    Accept,

    /// <summary>User declined rules in chat</summary>
    Deny,

    /// <summary>User accepted rules via DM</summary>
    DmAccept
}
