using Humanizer;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.Telegram.Services.Welcome;

/// <summary>
/// Pure static functions for building welcome messages as entity-based <see cref="TelegramMessage"/>s.
/// Admin-authored templates carry <c>{username}</c>, <c>{chat_name}</c>, and <c>{timeout}</c> tokens;
/// <see cref="TelegramMessageBuilder.AppendTemplate"/> substitutes them (username → clickable
/// <c>text_mention</c>) and passes any mistyped token through as literal text so it renders visibly.
/// </summary>
public static class WelcomeMessageBuilder
{
    /// <summary>
    /// Formats the welcome message. Uses DmChatTeaserMessage for DM-based modes (DmWelcome,
    /// EntranceExam) and MainWelcomeMessage for ChatAcceptDeny.
    /// </summary>
    public static TelegramMessage FormatWelcomeMessage(
        WelcomeConfig config,
        UserIdentity user,
        string chatName)
    {
        var template = config.Mode is WelcomeMode.DmWelcome or WelcomeMode.EntranceExam
            ? config.DmChatTeaserMessage
            : config.MainWelcomeMessage;

        return BuildFromTemplate(template, user, chatName, config.TimeoutSeconds);
    }

    /// <summary>
    /// Formats the rules confirmation message sent after a user accepts (MainWelcomeMessage plus footer).
    /// </summary>
    public static TelegramMessage FormatRulesConfirmation(
        WelcomeConfig config,
        UserIdentity user,
        string chatName)
        => new TelegramMessageBuilder()
            .AppendTemplate(config.MainWelcomeMessage, Substitutions(user, chatName, config.TimeoutSeconds))
            .Text("\n\n✅ You're all set! You can now participate in the chat.")
            .Build();

    /// <summary>
    /// Formats the DM acceptance confirmation message. No user mention, so plain text.
    /// </summary>
    public static string FormatDmAcceptanceConfirmation(string chatName)
        => $"✅ Welcome! You can now participate in {chatName}.";

    /// <summary>
    /// Formats the exam intro message (MainWelcomeMessage with substitution) shown in EntranceExam
    /// mode as the first DM before questions. No footer, no buttons.
    /// </summary>
    public static TelegramMessage FormatExamIntro(
        WelcomeConfig config,
        UserIdentity user,
        string chatName)
        => BuildFromTemplate(config.MainWelcomeMessage, user, chatName, config.TimeoutSeconds);

    private static TelegramMessage BuildFromTemplate(
        string template,
        UserIdentity user,
        string chatName,
        int timeoutSeconds)
        => new TelegramMessageBuilder()
            .AppendTemplate(template, Substitutions(user, chatName, timeoutSeconds))
            .Build();

    private static Dictionary<string, Action<TelegramMessageBuilder>> Substitutions(
        UserIdentity user,
        string chatName,
        int timeoutSeconds)
    {
        var formattedTimeout = TimeSpan.FromSeconds(timeoutSeconds).Humanize(precision: 2);
        return new Dictionary<string, Action<TelegramMessageBuilder>>
        {
            ["{username}"] = b => b.Mention(user),
            ["{chat_name}"] = b => b.Text(chatName),
            ["{timeout}"] = b => b.Text(formattedTimeout),
        };
    }
}
