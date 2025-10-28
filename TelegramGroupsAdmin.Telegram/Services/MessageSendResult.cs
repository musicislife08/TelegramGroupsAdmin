namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Result of a message send attempt
/// </summary>
public record MessageSendResult(
    long UserId,
    bool Success,
    MessageDeliveryMethod DeliveryMethod,
    string? ErrorMessage = null);

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
