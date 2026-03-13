namespace TelegramGroupsAdmin.Telegram.Services.Welcome;

/// <summary>
/// Parsed /start payload from a welcome deep link.
/// </summary>
/// <param name="ChatId">Group chat ID</param>
/// <param name="UserId">Target user ID</param>
public record WelcomeStartPayload(long ChatId, long UserId);
