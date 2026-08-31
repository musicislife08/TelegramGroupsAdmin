using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.Telegram.Services.Bot;

/// <summary>
/// Centralized DM delivery service with consistent bot_dm_enabled tracking and fallback handling.
/// Used by: NotificationSystem, WelcomeService, and any other feature that needs DM delivery.
/// This service is in the Bot layer and can use IBotMessageHandler directly.
///
/// Callers pass a <see cref="UserIdentity"/> so the service never needs to fetch the user for
/// logging — identity flows through from the call site (build via <c>UserIdentity.FromAsync</c>
/// when only an ID is available).
/// </summary>
public interface IBotDmService
{
    /// <summary>
    /// Attempt to send a DM to a user. Updates bot_dm_enabled flag automatically.
    /// If DM fails and fallbackChatId is provided, posts message in chat with optional auto-delete.
    /// </summary>
    /// <param name="user">Target user identity (used for logging; Id is the DM chat)</param>
    /// <param name="messageText">Message text to send</param>
    /// <param name="fallbackChatId">Optional chat ID to post fallback message if DM fails (403)</param>
    /// <param name="autoDeleteSeconds">Optional seconds to auto-delete fallback message (uses Quartz.NET)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating success, fallback usage, or failure</returns>
    Task<DmDeliveryResult> SendDmAsync(
        UserIdentity user,
        string messageText,
        long? fallbackChatId = null,
        int? autoDeleteSeconds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Entity-based <see cref="SendDmAsync(UserIdentity, string, long?, int?, CancellationToken)"/>:
    /// sends pre-rendered text + entities (no parse_mode), preserving the 403 fallback-to-chat and
    /// auto-delete semantics. Canonical overload; the string overload forwards here.
    /// </summary>
    Task<DmDeliveryResult> SendDmAsync(
        UserIdentity user,
        TelegramMessage message,
        long? fallbackChatId = null,
        int? autoDeleteSeconds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Edit a DM text message with optional inline keyboard change.
    /// Used for updating review notification DMs after admin action (removes buttons, shows result).
    /// </summary>
    Task<Message> EditDmTextAsync(
        long dmChatId,
        int messageId,
        string text,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Edit a DM media message caption with optional inline keyboard change.
    /// Used for updating review notification DMs with photos/videos after admin action.
    /// </summary>
    Task<Message> EditDmCaptionAsync(
        long dmChatId,
        int messageId,
        string? caption,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a message from a DM conversation.
    /// Used for cleaning up exam question messages after user answers.
    /// </summary>
    Task DeleteDmMessageAsync(
        long dmChatId,
        int messageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a DM with an inline keyboard to a user.
    /// Does NOT queue on failure (keyboards can't be queued).
    /// Used for exam questions with answer buttons.
    /// </summary>
    Task<DmDeliveryResult> SendDmWithKeyboardAsync(
        UserIdentity user,
        string messageText,
        InlineKeyboardMarkup keyboard,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Entity-based DM with an inline keyboard (no parse_mode). Does NOT queue on failure.
    /// Canonical overload; the string overload forwards here.
    /// </summary>
    Task<DmDeliveryResult> SendDmWithKeyboardAsync(
        UserIdentity user,
        TelegramMessage message,
        InlineKeyboardMarkup keyboard,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempt to send a DM using pre-rendered text + entities (no parse_mode).
    /// For admin notifications that need text_mention entities so user mentions are
    /// clickable even when the recipient has never interacted with the mentioned user.
    /// If DM fails (403), queues the message for later delivery (text only; entities dropped).
    /// </summary>
    Task<DmDeliveryResult> SendDmWithEntitiesAsync(
        UserIdentity user,
        string notificationType,
        string text,
        IReadOnlyList<MessageEntity> entities,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempt to send a DM with media and an entity-based caption (no parse_mode, no keyboard).
    /// If DM fails (403), queues the text for later delivery (without media/entities).
    /// </summary>
    Task<DmDeliveryResult> SendDmWithMediaEntitiesAsync(
        UserIdentity user,
        string notificationType,
        TelegramMessage message,
        string? photoPath = null,
        string? videoPath = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempt to send a DM with media, entities, and optional inline keyboard (no parse_mode).
    /// If DM fails (403), queues the text for later delivery (without media/buttons/entities).
    /// </summary>
    Task<DmDeliveryResult> SendDmWithMediaAndKeyboardEntitiesAsync(
        UserIdentity user,
        string notificationType,
        string text,
        IReadOnlyList<MessageEntity> entities,
        string? photoPath = null,
        string? videoPath = null,
        InlineKeyboardMarkup? keyboard = null,
        CancellationToken cancellationToken = default);
}
