using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Intents;

/// <summary>
/// Intent to mark a message as spam, delete it, and ban the sender.
/// This is a composite action that coordinates multiple handlers.
/// </summary>
/// <param name="MessageId">Message to mark as spam.</param>
/// <param name="UserId">User who sent the spam message.</param>
/// <param name="ChatId">Chat where the spam was detected.</param>
/// <param name="Executor">Who requested this action.</param>
/// <param name="Reason">Reason for marking as spam.</param>
/// <param name="TelegramMessage">Optional Telegram message object for training data extraction.</param>
public record MarkAsSpamIntent(
    long MessageId,
    long UserId,
    long ChatId,
    Actor Executor,
    string Reason,
    Message? TelegramMessage = null) : IActionIntent;
