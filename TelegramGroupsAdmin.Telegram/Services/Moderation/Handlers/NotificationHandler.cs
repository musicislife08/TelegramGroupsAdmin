using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;
using TelegramGroupsAdmin.Telegram.Services.Notifications;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Handlers;

/// <summary>
/// Domain expert for all moderation-related notifications.
/// Handles both user DM notifications and admin notifications via NotificationService.
/// Called directly by orchestrator after successful actions.
///
/// User DM notifications:
/// - Warnings: Sent to user with warning count
/// - Temp bans: Sent to user with duration and rejoin links
///
/// Admin notifications:
/// - Bans: Sent to admins subscribed to UserBanned event type
/// - Spam bans: Rich notification with message preview, detection details, media
/// </summary>
public class NotificationHandler : INotificationHandler
{
    private readonly INotificationOrchestrator _notificationOrchestrator;
    private readonly INotificationService _notificationService;
    private readonly IManagedChatsRepository _managedChatsRepository;
    private readonly IBotChatService _chatService;
    private readonly IChatCache _chatCache;
    private readonly ILogger<NotificationHandler> _logger;

    public NotificationHandler(
        INotificationOrchestrator notificationOrchestrator,
        INotificationService notificationService,
        IManagedChatsRepository managedChatsRepository,
        IBotChatService chatService,
        IChatCache chatCache,
        ILogger<NotificationHandler> logger)
    {
        _notificationOrchestrator = notificationOrchestrator;
        _notificationService = notificationService;
        _managedChatsRepository = managedChatsRepository;
        _chatService = chatService;
        _chatCache = chatCache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<NotificationResult> NotifyUserWarningAsync(
        UserIdentity user,
        int warningCount,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var success = await SendWarningNotificationAsync(user, warningCount, reason, cancellationToken);
            return success
                ? NotificationResult.Succeeded()
                : NotificationResult.Failed("Notification delivery failed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to send warning notification for user {User}",
                user.ToLogDebug());
            return NotificationResult.Failed(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<NotificationResult> NotifyUserTempBanAsync(
        UserIdentity user,
        TimeSpan duration,
        DateTimeOffset expiresAt,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var success = await SendTempBanNotificationAsync(user, duration, expiresAt, reason, cancellationToken);
            return success
                ? NotificationResult.Succeeded()
                : NotificationResult.Failed("Notification delivery failed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to send temp-ban notification for user {User}",
                user.ToLogDebug());
            return NotificationResult.Failed(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<NotificationResult> NotifyAdminsBanAsync(
        UserIdentity user,
        Actor executor,
        string? reason,
        ChatIdentity? chat = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await SendBanAdminNotificationAsync(user, executor, reason, chat, cancellationToken);
            return NotificationResult.Succeeded();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to send ban admin notification for user {User}",
                user.ToLogDebug());
            return NotificationResult.Failed(ex.Message);
        }
    }

    /// <summary>
    /// Send warning DM to the user.
    /// </summary>
    /// <returns>True if the notification was sent successfully.</returns>
    private async Task<bool> SendWarningNotificationAsync(UserIdentity user, int warningCount, string? reason, CancellationToken cancellationToken)
    {
        var message = $"⚠️ <b>Warning Issued</b>\n\n" +
                      $"You have received a warning.\n\n" +
                      $"<b>Reason:</b> {EscapeHtml(reason)}\n" +
                      $"<b>Total Warnings:</b> {warningCount}\n\n" +
                      $"Please review the group rules and avoid similar behavior in the future.\n\n" +
                      $"💡 Use /mystatus to check your current status.";

        var notification = new Notification("warning", message);
        var result = await _notificationOrchestrator.SendTelegramDmAsync(user.Id, notification, cancellationToken);

        if (result.Success)
        {
            _logger.LogInformation(
                "Sent warning notification to {User} (warning #{Count})",
                user.ToLogInfo(), warningCount);
        }
        else
        {
            _logger.LogWarning(
                "Failed to send warning notification to {User}: {Error}",
                user.ToLogDebug(), result.ErrorMessage ?? "Unknown error");
        }

        return result.Success;
    }

    /// <summary>
    /// Send temp ban DM to the user with rejoin links.
    /// </summary>
    /// <returns>True if the notification was sent successfully.</returns>
    private async Task<bool> SendTempBanNotificationAsync(UserIdentity user, TimeSpan duration, DateTimeOffset expiresAt, string? reason, CancellationToken cancellationToken)
    {
        // Get all active managed chats for rejoin links
        var allChats = await _managedChatsRepository.GetAllChatsAsync(cancellationToken: cancellationToken);
        var activeChats = allChats.Where(c => c.IsActive && !c.IsDeleted).ToList();

        // Build notification message
        var notificationMessage = $"⏱️ <b>You have been temporarily banned</b>\n\n" +
                          $"<b>Reason:</b> {EscapeHtml(reason)}\n" +
                          $"<b>Duration:</b> {TimeSpanUtilities.FormatDuration(duration)}\n" +
                          $"<b>Expires:</b> {expiresAt:yyyy-MM-dd HH:mm} UTC\n\n" +
                          $"You will be automatically unbanned after this time.";

        // Collect invite links for all active chats
        var inviteLinkSection = await BuildInviteLinkSectionAsync(activeChats, cancellationToken);
        if (!string.IsNullOrEmpty(inviteLinkSection))
        {
            notificationMessage += $"\n\n**Rejoin Links:**\n{inviteLinkSection}";
        }

        var notification = new Notification("tempban", notificationMessage);
        var result = await _notificationOrchestrator.SendTelegramDmAsync(user.Id, notification, cancellationToken);

        if (result.Success)
        {
            _logger.LogInformation(
                "Sent temp-ban notification to {User} (expires: {ExpiresAt})",
                user.ToLogInfo(), expiresAt);
        }
        else
        {
            _logger.LogWarning(
                "Failed to send temp-ban notification to {User}: {Error}",
                user.ToLogDebug(), result.ErrorMessage ?? "Unknown error");
        }

        return result.Success;
    }

    /// <summary>
    /// Send ban notification to admins via typed notification service.
    /// </summary>
    private async Task SendBanAdminNotificationAsync(UserIdentity user, Actor executor, string? reason, ChatIdentity? chat, CancellationToken cancellationToken)
    {
        await _notificationService.SendBanNotificationAsync(
            user: user,
            executor: executor,
            reason: reason,
            chat: chat,
            ct: cancellationToken);

        _logger.LogDebug(
            "Dispatched UserBanned admin notification for {User} (chat: {Chat})",
            user.ToLogDebug(), chat?.ToLogDebug() ?? "global");
    }

    /// <summary>
    /// Build invite link section for rejoin notifications.
    /// Uses cached SDK Chat objects (populated by health checks on startup).
    /// </summary>
    private async Task<string> BuildInviteLinkSectionAsync(
        List<ManagedChatRecord> activeChats,
        CancellationToken cancellationToken)
    {
        var inviteLinks = new List<string>();

        foreach (var managedChat in activeChats)
        {
            try
            {
                var inviteLink = await _chatService.GetInviteLinkAsync(managedChat.Identity.Id, cancellationToken);
                if (!string.IsNullOrEmpty(inviteLink))
                {
                    // Use chat name from managed chat record, or fall back to chatId
                    var chatDisplayName = managedChat.Identity.ChatName ?? managedChat.Identity.Id.ToString();
                    inviteLinks.Add($"• [{chatDisplayName}]({inviteLink})");
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "Failed to get invite link for chat {ChatId}, skipping from notification",
                    managedChat.Identity.Id);
            }
        }

        return inviteLinks.Count > 0 ? string.Join("\n", inviteLinks) : string.Empty;
    }

    /// <summary>
    /// Escapes HTML special characters to prevent formatting issues in user-facing notifications.
    /// Uses HTML mode for Telegram messages which is more standard and easier to escape correctly.
    /// TODO: When rich formatting support is added (GitHub issue to be created), consider using
    /// a proper sanitization library like HtmlSanitizer to allow safe user-provided HTML/Markdown.
    /// </summary>
    private static string EscapeHtml(string? text) =>
        string.IsNullOrEmpty(text)
            ? string.Empty
            : System.Net.WebUtility.HtmlEncode(text);

    /// <inheritdoc />
    public async Task<NotificationResult> NotifyAdminsSpamBanAsync(
        MessageWithDetectionHistory enrichedMessage,
        int chatsAffected,
        bool messageDeleted,
        CancellationToken cancellationToken = default)
    {
        var msg = enrichedMessage.Message;
        var detection = enrichedMessage.LatestDetection;

        try
        {
            // Build message preview (use translated text if available)
            var messageContent = msg.Translation?.TranslatedText ?? msg.MessageText;
            var messagePreview = messageContent != null && messageContent.Length > 100
                ? messageContent[..97] + "..."
                : messageContent;

            // Truncate detection reason
            var detectionReason = detection?.Reason is { Length: > 100 }
                ? detection.Reason[..97] + "..."
                : detection?.Reason;

            // Get media paths from enriched message
            string? photoPath = null;
            string? videoPath = null;

            if (!string.IsNullOrEmpty(msg.PhotoLocalPath))
            {
                photoPath = msg.PhotoLocalPath;
            }
            else if (msg.MediaType is MediaType.Video or MediaType.Animation
                     && !string.IsNullOrEmpty(msg.MediaLocalPath))
            {
                videoPath = msg.MediaLocalPath;
            }

            await _notificationService.SendSpamBanNotificationAsync(
                chat: msg.Chat,
                user: msg.User,
                bannedBy: detection?.AddedBy,
                netConfidence: detection != null ? Math.Abs(detection.NetConfidence) : 0,
                confidence: detection?.Confidence ?? 0,
                detectionReason: detectionReason,
                chatsAffected: chatsAffected,
                messageDeleted: messageDeleted,
                messageId: msg.MessageId,
                messagePreview: messagePreview,
                photoPath: photoPath,
                videoPath: videoPath,
                ct: cancellationToken);

            _logger.LogDebug(
                "Dispatched spam ban notification for message {MessageId}",
                msg.MessageId);

            return NotificationResult.Succeeded();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to send spam ban notification for message {MessageId}",
                msg.MessageId);
            return NotificationResult.Failed(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<NotificationResult> NotifyUserCriticalViolationAsync(
        UserIdentity user,
        IReadOnlyList<string> violations,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Build violation list
            var violationList = string.Join("\n", violations.Select((v, i) => $"{i + 1}. {EscapeHtml(v)}"));

            var message = $"⚠️ <b>Message Removed</b>\n\n" +
                          $"Your message was deleted due to security policy violations:\n\n" +
                          $"{violationList}\n\n" +
                          $"These checks apply to all users regardless of trust status.\n\n" +
                          $"💡 If you believe this was a mistake, please contact an admin.";

            var notification = new Notification("critical_violation", message);
            var result = await _notificationOrchestrator.SendTelegramDmAsync(user.Id, notification, cancellationToken);

            if (result.Success)
            {
                _logger.LogInformation(
                    "Sent critical violation notification to {User} ({Count} violations)",
                    user.ToLogInfo(), violations.Count);
            }
            else
            {
                _logger.LogWarning(
                    "Failed to send critical violation notification to {User}: {Error}",
                    user.ToLogDebug(), result.ErrorMessage ?? "Unknown error");
            }

            return result.Success
                ? NotificationResult.Succeeded()
                : NotificationResult.Failed(result.ErrorMessage ?? "Notification delivery failed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to send critical violation notification for user {User}",
                user.ToLogDebug());
            return NotificationResult.Failed(ex.Message);
        }
    }
}
