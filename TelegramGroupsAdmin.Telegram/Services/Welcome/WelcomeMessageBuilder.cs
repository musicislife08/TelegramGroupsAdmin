using Humanizer;

namespace TelegramGroupsAdmin.Telegram.Services.Welcome;

/// <summary>
/// Pure static functions for building welcome message text.
/// No side effects, no Telegram API dependencies - 100% testable.
/// </summary>
public static class WelcomeMessageBuilder
{
    /// <summary>
    /// Formats the welcome message with variable substitution.
    /// Uses MainWelcomeMessage for ChatAcceptDeny mode, DmChatTeaserMessage for DM-based modes (DmWelcome, EntranceExam).
    /// </summary>
    /// <param name="config">Welcome configuration containing message templates</param>
    /// <param name="username">User's @username or first name if no username</param>
    /// <param name="chatName">Display name of the chat</param>
    /// <returns>Formatted message text with all variables substituted</returns>
    public static string FormatWelcomeMessage(
        WelcomeConfig config,
        string username,
        string chatName)
    {
        // DM-based modes (DmWelcome, EntranceExam) show teaser in group, main content in DM
        // ChatAcceptDeny shows main message directly in group
        var template = config.Mode is WelcomeMode.DmWelcome or WelcomeMode.EntranceExam
            ? config.DmChatTeaserMessage
            : config.MainWelcomeMessage;

        return SubstituteVariables(template, username, chatName, config.TimeoutSeconds);
    }

    /// <summary>
    /// Formats the rules confirmation message sent after user accepts.
    /// Uses MainWelcomeMessage with a confirmation footer appended.
    /// </summary>
    /// <param name="config">Welcome configuration containing message templates</param>
    /// <param name="username">User's @username or first name if no username</param>
    /// <param name="chatName">Display name of the chat</param>
    /// <returns>Formatted message with confirmation footer</returns>
    public static string FormatRulesConfirmation(
        WelcomeConfig config,
        string username,
        string chatName)
    {
        var baseMessage = SubstituteVariables(
            config.MainWelcomeMessage,
            username,
            chatName,
            config.TimeoutSeconds);

        return baseMessage + "\n\n✅ You're all set! You can now participate in the chat.";
    }

    /// <summary>
    /// Formats the DM acceptance confirmation message.
    /// Sent to user in DM after they accept rules via deep link.
    /// </summary>
    /// <param name="chatName">Display name of the chat</param>
    /// <returns>Confirmation message text</returns>
    public static string FormatDmAcceptanceConfirmation(string chatName)
    {
        return $"✅ Welcome! You can now participate in {chatName}.";
    }

    /// <summary>
    /// Formats the warning message shown when wrong user clicks a button.
    /// </summary>
    /// <param name="username">Username of the user who clicked incorrectly</param>
    /// <returns>Warning message text</returns>
    public static string FormatWrongUserWarning(string username)
    {
        return $"{username}, ⚠️ this button is not for you. Only the mentioned user can respond.";
    }

    /// <summary>
    /// Formats the verifying message shown while security checks run.
    /// This message is displayed briefly (~2s) while CAS/impersonation checks execute.
    /// </summary>
    /// <param name="username">User's @username or first name</param>
    /// <returns>Verifying message text</returns>
    public static string FormatVerifyingMessage(string username)
    {
        return $"{username} ⏳ Verifying...";
    }

    /// <summary>
    /// Formats the exam intro message (MainWelcomeMessage with variable substitution).
    /// Used in EntranceExam mode as the first DM message before questions.
    /// No footer, no buttons - just the rules/guidelines.
    /// </summary>
    /// <param name="config">Welcome configuration containing message templates</param>
    /// <param name="username">User's @username or first name if no username</param>
    /// <param name="chatName">Display name of the chat</param>
    /// <returns>Formatted message text with variables substituted</returns>
    public static string FormatExamIntro(
        WelcomeConfig config,
        string username,
        string chatName)
    {
        return SubstituteVariables(
            config.MainWelcomeMessage,
            username,
            chatName,
            config.TimeoutSeconds);
    }

    private static string SubstituteVariables(
        string template,
        string username,
        string chatName,
        int timeoutSeconds)
    {
        var formattedTimeout = TimeSpan.FromSeconds(timeoutSeconds).Humanize(precision: 2);

        return template
            .Replace("{username}", username)
            .Replace("{chat_name}", chatName)
            .Replace("{timeout}", formattedTimeout);
    }
}
