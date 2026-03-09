namespace TelegramGroupsAdmin.Telegram.Services.Welcome;

/// <summary>
/// Parsed welcome callback data.
/// </summary>
/// <param name="Type">Type of callback action</param>
/// <param name="UserId">Target user ID</param>
/// <param name="ChatId">Group chat ID (only for DmAccept)</param>
public record WelcomeCallbackData(
    WelcomeCallbackType Type,
    long UserId,
    long? ChatId = null);
