namespace TelegramGroupsAdmin.Telegram.Services.Notifications;

/// <summary>
/// Result of a notification delivery attempt
/// </summary>
/// <param name="Success">True if the notification was delivered successfully</param>
/// <param name="ErrorMessage">Optional error message if delivery failed</param>
public record DeliveryResult(bool Success, string? ErrorMessage = null);
