namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Result of a message send attempt
/// </summary>
public record MessageSendResult(
    long UserId,
    bool Success,
    MessageDeliveryMethod DeliveryMethod,
    string? ErrorMessage = null);
