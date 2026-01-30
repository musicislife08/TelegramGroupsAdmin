using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation;

/// <summary>
/// Orchestrates moderation actions across Telegram and database.
/// Handles bans, warnings, trust status, and message deletion with audit logging.
/// The "boss" that knows all workers, decides who to call, and owns business rules.
/// </summary>
public interface IModerationOrchestrator
{
    /// <summary>
    /// Mark message as spam, delete it, ban user globally, and revoke trust.
    /// Composes: EnsureExists → Delete → Ban → Training Data
    /// </summary>
    /// <param name="messageId">The message ID to mark as spam.</param>
    /// <param name="userId">The Telegram user ID of the message author.</param>
    /// <param name="chatId">The chat ID where the message was posted.</param>
    /// <param name="executor">The actor performing this action.</param>
    /// <param name="reason">Reason for marking as spam.</param>
    /// <param name="telegramMessage">Optional Telegram message object for backfill.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success and affected chats/actions.</returns>
    Task<ModerationResult> MarkAsSpamAndBanAsync(
        long messageId,
        long userId,
        long chatId,
        Actor executor,
        string reason,
        Message? telegramMessage = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ban user globally across all managed chats.
    /// Business rule: Bans always revoke trust.
    /// </summary>
    /// <param name="userId">The Telegram user ID to ban.</param>
    /// <param name="messageId">Optional message ID that triggered the ban.</param>
    /// <param name="executor">The actor performing this action.</param>
    /// <param name="reason">Reason for the ban.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success and affected chats.</returns>
    Task<ModerationResult> BanUserAsync(
        long userId,
        long? messageId,
        Actor executor,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Warn user globally with automatic ban after threshold.
    /// Business rule: N warnings = auto-ban (configurable per chat).
    /// </summary>
    /// <param name="userId">The Telegram user ID to warn.</param>
    /// <param name="messageId">Optional message ID that triggered the warning.</param>
    /// <param name="executor">The actor performing this action.</param>
    /// <param name="reason">Reason for the warning.</param>
    /// <param name="chatId">The chat ID for chat-specific warning threshold configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success, warning count, and whether auto-ban was triggered.</returns>
    Task<ModerationResult> WarnUserAsync(
        long userId,
        long? messageId,
        Actor executor,
        string reason,
        long chatId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Trust user globally (bypass spam detection).
    /// </summary>
    /// <param name="userId">The Telegram user ID to trust.</param>
    /// <param name="executor">The actor performing this action.</param>
    /// <param name="reason">Reason for trusting the user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success.</returns>
    Task<ModerationResult> TrustUserAsync(
        long userId,
        Actor executor,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove trust from user globally.
    /// </summary>
    /// <param name="userId">The Telegram user ID to untrust.</param>
    /// <param name="executor">The actor performing this action.</param>
    /// <param name="reason">Reason for removing trust.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success.</returns>
    Task<ModerationResult> UntrustUserAsync(
        long userId,
        Actor executor,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unban user globally and optionally restore trust.
    /// </summary>
    /// <param name="userId">The Telegram user ID to unban.</param>
    /// <param name="executor">The actor performing this action.</param>
    /// <param name="reason">Reason for the unban.</param>
    /// <param name="restoreTrust">Whether to restore trust after unbanning (for false positive corrections).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success and affected chats.</returns>
    Task<ModerationResult> UnbanUserAsync(
        long userId,
        Actor executor,
        string reason,
        bool restoreTrust = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a message from Telegram and mark as deleted in database.
    /// </summary>
    /// <param name="messageId">The message ID to delete.</param>
    /// <param name="chatId">The chat ID containing the message.</param>
    /// <param name="userId">The Telegram user ID of the message author.</param>
    /// <param name="deletedBy">The actor performing the deletion.</param>
    /// <param name="reason">Optional reason for deletion.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating whether the message was deleted.</returns>
    Task<ModerationResult> DeleteMessageAsync(
        long messageId,
        long chatId,
        long userId,
        Actor deletedBy,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Temporarily ban user globally with automatic unrestriction.
    /// </summary>
    /// <param name="userId">The Telegram user ID to temp ban.</param>
    /// <param name="messageId">Optional message ID that triggered the temp ban.</param>
    /// <param name="executor">The actor performing this action.</param>
    /// <param name="reason">Reason for the temp ban.</param>
    /// <param name="duration">Duration of the temporary ban.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success and affected chats.</returns>
    Task<ModerationResult> TempBanUserAsync(
        long userId,
        long? messageId,
        Actor executor,
        string reason,
        TimeSpan duration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restrict user (mute) globally or in a specific chat with automatic unrestriction.
    /// </summary>
    /// <param name="userId">The Telegram user ID to restrict.</param>
    /// <param name="messageId">Optional message ID that triggered the restriction.</param>
    /// <param name="executor">The actor performing this action.</param>
    /// <param name="reason">Reason for the restriction.</param>
    /// <param name="duration">Duration of the restriction.</param>
    /// <param name="chatId">Optional chat ID for chat-specific restriction; null for global.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success and affected chats.</returns>
    Task<ModerationResult> RestrictUserAsync(
        long userId,
        long? messageId,
        Actor executor,
        string reason,
        TimeSpan duration,
        long? chatId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sync an existing global ban to a specific chat (lazy sync for chats added after ban).
    /// Use when a globally banned user joins or posts in a chat that was added after their ban.
    /// </summary>
    /// <param name="user">The Telegram User object to ban.</param>
    /// <param name="chat">The Telegram Chat to sync the ban to.</param>
    /// <param name="reason">Reason for the ban sync.</param>
    /// <param name="triggeredByMessageId">Optional message ID that triggered the sync.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success and affected chats.</returns>
    Task<ModerationResult> SyncBanToChatAsync(
        User user,
        Chat chat,
        string reason,
        long? triggeredByMessageId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restore user permissions to the chat's default permissions.
    /// Used when approving users through welcome/exam flows.
    /// </summary>
    /// <param name="userId">The Telegram user ID to restore permissions for.</param>
    /// <param name="chatId">The chat ID to restore permissions in.</param>
    /// <param name="executor">The actor performing this action.</param>
    /// <param name="reason">Reason for restoring permissions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success.</returns>
    Task<ModerationResult> RestoreUserPermissionsAsync(
        long userId,
        long chatId,
        Actor executor,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Kick user from a specific chat (ban then immediately unban).
    /// Does not affect other chats or create permanent ban record.
    /// Used for welcome flow denials and exam failures.
    /// </summary>
    /// <param name="userId">The Telegram user ID to kick.</param>
    /// <param name="chatId">The chat to kick the user from.</param>
    /// <param name="executor">The actor performing this action.</param>
    /// <param name="reason">Reason for the kick.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success.</returns>
    Task<ModerationResult> KickUserFromChatAsync(
        long userId,
        long chatId,
        Actor executor,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Handle malware detection: delete message, create admin report, notify admins.
    /// Does NOT auto-ban (malware upload may be accidental).
    /// </summary>
    /// <param name="messageId">The message ID containing malware.</param>
    /// <param name="chatId">The chat ID where the message was posted.</param>
    /// <param name="userId">The Telegram user ID of the message author.</param>
    /// <param name="malwareDetails">Details about the detected malware.</param>
    /// <param name="telegramMessage">Optional Telegram message object for backfill.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success and whether the message was deleted.</returns>
    Task<ModerationResult> HandleMalwareViolationAsync(
        long messageId,
        long chatId,
        long userId,
        string malwareDetails,
        Message? telegramMessage = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Handle critical check violation by trusted/admin user: delete message, notify user.
    /// Does NOT ban/warn (trusted users get a pass, but message still removed).
    /// Critical checks (URL filtering, file scanning) bypass trust status.
    /// </summary>
    /// <param name="messageId">The message ID that violated critical check.</param>
    /// <param name="chatId">The chat ID where the message was posted.</param>
    /// <param name="userId">The Telegram user ID of the message author.</param>
    /// <param name="violations">List of critical check violations.</param>
    /// <param name="telegramMessage">Optional Telegram message object for context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success and whether the message was deleted.</returns>
    Task<ModerationResult> HandleCriticalViolationAsync(
        long messageId,
        long chatId,
        long userId,
        List<string> violations,
        Message? telegramMessage = null,
        CancellationToken cancellationToken = default);
}
