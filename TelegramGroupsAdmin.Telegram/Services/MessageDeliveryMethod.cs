namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Message delivery method classification
/// </summary>
public enum MessageDeliveryMethod
{
    /// <summary>Message sent via private DM</summary>
    PrivateDm,

    /// <summary>Message sent as chat mention (fallback)</summary>
    ChatMention,

    /// <summary>Both DM and fallback failed</summary>
    Failed
}
