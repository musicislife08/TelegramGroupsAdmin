using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramGroupsAdmin.Telegram.Services.Welcome;

/// <summary>
/// Pure static functions for building welcome inline keyboards.
/// Returns Telegram InlineKeyboardMarkup objects with correct callback data.
/// No side effects, no API calls - 100% testable.
/// </summary>
public static class WelcomeKeyboardBuilder
{
    /// <summary>
    /// Builds the Accept/Deny keyboard for chat mode welcome messages.
    /// </summary>
    /// <param name="config">Welcome configuration with button text</param>
    /// <param name="userId">Target user ID (embedded in callback data)</param>
    /// <returns>Inline keyboard with Accept and Deny buttons</returns>
    public static InlineKeyboardMarkup BuildChatModeKeyboard(WelcomeConfig config, long userId)
    {
        return new InlineKeyboardMarkup([
            [
                InlineKeyboardButton.WithCallbackData(config.AcceptButtonText, $"welcome_accept:{userId}"),
                InlineKeyboardButton.WithCallbackData(config.DenyButtonText, $"welcome_deny:{userId}")
            ]
        ]);
    }

    /// <summary>
    /// Builds the deep link keyboard for DM mode welcome messages.
    /// User clicks to open DM with bot and receive rules privately.
    /// </summary>
    /// <param name="config">Welcome configuration with button text</param>
    /// <param name="chatId">Group chat ID (for deep link payload)</param>
    /// <param name="userId">Target user ID (for deep link payload)</param>
    /// <param name="botUsername">Bot username (without @)</param>
    /// <returns>Inline keyboard with single URL button</returns>
    public static InlineKeyboardMarkup BuildDmModeKeyboard(
        WelcomeConfig config,
        long chatId,
        long userId,
        string botUsername)
    {
        var deepLink = WelcomeDeepLinkBuilder.BuildStartDeepLink(botUsername, chatId, userId);

        return new InlineKeyboardMarkup([
            [
                InlineKeyboardButton.WithUrl(config.DmButtonText, deepLink)
            ]
        ]);
    }

    /// <summary>
    /// Builds the "Return to Chat" keyboard shown after DM acceptance.
    /// Provides deep link back to the group chat.
    /// </summary>
    /// <param name="chatName">Display name of the chat</param>
    /// <param name="chatLink">Deep link URL to the chat</param>
    /// <returns>Inline keyboard with single URL button</returns>
    public static InlineKeyboardMarkup BuildReturnToChatKeyboard(string chatName, string chatLink)
    {
        return new InlineKeyboardMarkup([
            [
                InlineKeyboardButton.WithUrl($"💬 Return to {chatName}", chatLink)
            ]
        ]);
    }

    /// <summary>
    /// Builds the Accept button keyboard for DM welcome flow.
    /// User clicks to accept rules after reading them in DM.
    /// </summary>
    /// <param name="config">Welcome configuration with button text</param>
    /// <param name="groupChatId">Group chat ID (for callback data)</param>
    /// <param name="userId">Target user ID (for callback data)</param>
    /// <returns>Inline keyboard with single callback button</returns>
    public static InlineKeyboardMarkup BuildDmAcceptKeyboard(
        WelcomeConfig config,
        long groupChatId,
        long userId)
    {
        return new InlineKeyboardMarkup([
            [
                InlineKeyboardButton.WithCallbackData(
                    config.AcceptButtonText,
                    $"dm_accept:{groupChatId}:{userId}")
            ]
        ]);
    }

    /// <summary>
    /// Builds the deep link keyboard for entrance exam mode.
    /// User clicks to open DM with bot and start the exam.
    /// Reuses DmButtonText from config for button text.
    /// </summary>
    /// <param name="config">Welcome configuration with button text</param>
    /// <param name="chatId">Group chat ID (for deep link payload)</param>
    /// <param name="userId">Target user ID (for deep link payload)</param>
    /// <param name="botUsername">Bot username (without @)</param>
    /// <returns>Inline keyboard with single URL button</returns>
    public static InlineKeyboardMarkup BuildExamModeKeyboard(
        WelcomeConfig config,
        long chatId,
        long userId,
        string botUsername)
    {
        var deepLink = WelcomeDeepLinkBuilder.BuildExamStartDeepLink(botUsername, chatId, userId);

        return new InlineKeyboardMarkup([
            [
                InlineKeyboardButton.WithUrl(config.DmButtonText, deepLink)
            ]
        ]);
    }
}
