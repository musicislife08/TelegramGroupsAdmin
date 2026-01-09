using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
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
/// </summary>
public class NotificationHandler : INotificationHandler
{
    private readonly INotificationOrchestrator _notificationOrchestrator;
    private readonly INotificationService _notificationService;
    private readonly IManagedChatsRepository _managedChatsRepository;
    private readonly ITelegramUserRepository _telegramUserRepository;
    private readonly IChatInviteLinkService _chatInviteLinkService;
    private readonly IChatCache _chatCache;
    private readonly ILogger<NotificationHandler> _logger;

    public NotificationHandler(
        INotificationOrchestrator notificationOrchestrator,
        INotificationService notificationService,
        IManagedChatsRepository managedChatsRepository,
        ITelegramUserRepository telegramUserRepository,
        IChatInviteLinkService chatInviteLinkService,
        IChatCache chatCache,
        ILogger<NotificationHandler> logger)
    {
        _notificationOrchestrator = notificationOrchestrator;
        _notificationService = notificationService;
        _managedChatsRepository = managedChatsRepository;
        _telegramUserRepository = telegramUserRepository;
        _chatInviteLinkService = chatInviteLinkService;
        _chatCache = chatCache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<NotificationResult> NotifyUserWarningAsync(
        long userId,
        int warningCount,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        // User must exist - they sent a message to trigger this warning
        var user = await _telegramUserRepository.GetByTelegramIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException($"User {userId} not found in database. This indicates a bug in the moderation pipeline.");

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
        long userId,
        TimeSpan duration,
        DateTimeOffset expiresAt,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        // User must exist - they sent a message to trigger this temp ban
        var user = await _telegramUserRepository.GetByTelegramIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException($"User {userId} not found in database. This indicates a bug in the moderation pipeline.");

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
        long userId,
        Actor executor,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        // User must exist - they sent a message to trigger this ban
        var user = await _telegramUserRepository.GetByTelegramIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException($"User {userId} not found in database. This indicates a bug in the moderation pipeline.");

        try
        {
            await SendBanAdminNotificationAsync(user, executor, reason, cancellationToken);
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
    private async Task<bool> SendWarningNotificationAsync(TelegramUser user, int warningCount, string? reason, CancellationToken cancellationToken)
    {
        var message = $"⚠️ <b>Warning Issued</b>\n\n" +
                      $"You have received a warning.\n\n" +
                      $"<b>Reason:</b> {EscapeHtml(reason)}\n" +
                      $"<b>Total Warnings:</b> {warningCount}\n\n" +
                      $"Please review the group rules and avoid similar behavior in the future.\n\n" +
                      $"💡 Use /mystatus to check your current status.";

        var notification = new Notification("warning", message);
        var result = await _notificationOrchestrator.SendTelegramDmAsync(user.TelegramUserId, notification, cancellationToken);

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
    private async Task<bool> SendTempBanNotificationAsync(TelegramUser user, TimeSpan duration, DateTimeOffset expiresAt, string? reason, CancellationToken cancellationToken)
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
        var result = await _notificationOrchestrator.SendTelegramDmAsync(user.TelegramUserId, notification, cancellationToken);

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
    /// Send ban notification to admins subscribed to UserBanned events.
    /// </summary>
    private async Task SendBanAdminNotificationAsync(TelegramUser user, Actor executor, string? reason, CancellationToken cancellationToken)
    {
        var userInfo = TelegramDisplayName.Format(user.FirstName, user.LastName, user.Username, user.TelegramUserId);

        var subject = "User Banned";
        var message = $"User {userInfo} has been banned.\n\n" +
                      $"Reason: {reason}\n" +
                      $"Banned by: {executor.GetDisplayText()}";

        // Send as a system notification to owners
        await _notificationService.SendSystemNotificationAsync(
            NotificationEventType.UserBanned,
            subject,
            message,
            cancellationToken);

        _logger.LogDebug(
            "Dispatched UserBanned admin notification for {User}",
            user.ToLogDebug());
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
            // Look up SDK Chat from cache (populated by health checks on startup)
            var sdkChat = _chatCache.GetChat(managedChat.ChatId);
            if (sdkChat == null)
            {
                _logger.LogDebug(
                    "Chat {ChatId} not in cache, skipping invite link for notification",
                    managedChat.ChatId);
                continue;
            }

            try
            {
                var inviteLink = await _chatInviteLinkService.GetInviteLinkAsync(sdkChat, cancellationToken);
                if (!string.IsNullOrEmpty(inviteLink))
                {
                    inviteLinks.Add($"• [{sdkChat.ToLogInfo()}]({inviteLink})");
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "Failed to get invite link for {Chat}, skipping from notification",
                    sdkChat.ToLogDebug());
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
    private static string EscapeHtml(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // Escape HTML special characters for Telegram's HTML parseMode
        // Telegram HTML supports: <b>, <i>, <u>, <s>, <a>, <code>, <pre>
        return text
            .Replace("&", "&amp;")   // Must be first to avoid double-escaping
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }
}
