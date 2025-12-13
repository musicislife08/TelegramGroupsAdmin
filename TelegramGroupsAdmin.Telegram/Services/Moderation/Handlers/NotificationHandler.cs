using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Utilities;
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
    private readonly ILogger<NotificationHandler> _logger;

    public NotificationHandler(
        INotificationOrchestrator notificationOrchestrator,
        INotificationService notificationService,
        IManagedChatsRepository managedChatsRepository,
        ITelegramUserRepository telegramUserRepository,
        IChatInviteLinkService chatInviteLinkService,
        ILogger<NotificationHandler> logger)
    {
        _notificationOrchestrator = notificationOrchestrator;
        _notificationService = notificationService;
        _managedChatsRepository = managedChatsRepository;
        _telegramUserRepository = telegramUserRepository;
        _chatInviteLinkService = chatInviteLinkService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<NotificationResult> NotifyUserWarningAsync(
        long userId,
        int warningCount,
        string? reason,
        CancellationToken ct = default)
    {
        try
        {
            var success = await SendWarningNotificationAsync(userId, warningCount, reason, ct);
            return success
                ? NotificationResult.Succeeded()
                : NotificationResult.Failed("Notification delivery failed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to send warning notification for user {UserId}",
                userId);
            return NotificationResult.Failed(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<NotificationResult> NotifyUserTempBanAsync(
        long userId,
        TimeSpan duration,
        DateTimeOffset expiresAt,
        string? reason,
        CancellationToken ct = default)
    {
        try
        {
            var success = await SendTempBanNotificationAsync(userId, duration, expiresAt, reason, ct);
            return success
                ? NotificationResult.Succeeded()
                : NotificationResult.Failed("Notification delivery failed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to send temp-ban notification for user {UserId}",
                userId);
            return NotificationResult.Failed(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<NotificationResult> NotifyAdminsBanAsync(
        long userId,
        Actor executor,
        string? reason,
        CancellationToken ct = default)
    {
        try
        {
            await SendBanAdminNotificationAsync(userId, executor, reason, ct);
            return NotificationResult.Succeeded();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to send ban admin notification for user {UserId}",
                userId);
            return NotificationResult.Failed(ex.Message);
        }
    }

    /// <summary>
    /// Send warning DM to the user.
    /// </summary>
    /// <returns>True if the notification was sent successfully.</returns>
    private async Task<bool> SendWarningNotificationAsync(long userId, int warningCount, string? reason, CancellationToken ct)
    {
        var message = $"⚠️ <b>Warning Issued</b>\n\n" +
                      $"You have received a warning.\n\n" +
                      $"<b>Reason:</b> {EscapeHtml(reason)}\n" +
                      $"<b>Total Warnings:</b> {warningCount}\n\n" +
                      $"Please review the group rules and avoid similar behavior in the future.\n\n" +
                      $"💡 Use /mystatus to check your current status.";

        var notification = new Notification("warning", message);
        var result = await _notificationOrchestrator.SendTelegramDmAsync(userId, notification, ct);

        if (result.Success)
        {
            _logger.LogInformation(
                "Sent warning notification to user {UserId} (warning #{Count})",
                userId, warningCount);
        }
        else
        {
            _logger.LogWarning(
                "Failed to send warning notification to user {UserId}: {Error}",
                userId, result.ErrorMessage ?? "Unknown error");
        }

        return result.Success;
    }

    /// <summary>
    /// Send temp ban DM to the user with rejoin links.
    /// </summary>
    /// <returns>True if the notification was sent successfully.</returns>
    private async Task<bool> SendTempBanNotificationAsync(long userId, TimeSpan duration, DateTimeOffset expiresAt, string? reason, CancellationToken ct)
    {
        // Get all active managed chats for rejoin links
        var allChats = await _managedChatsRepository.GetAllChatsAsync(ct);
        var activeChats = allChats.Where(c => c.IsActive && !c.IsDeleted).ToList();

        // Build notification message
        var notificationMessage = $"⏱️ <b>You have been temporarily banned</b>\n\n" +
                          $"<b>Reason:</b> {EscapeHtml(reason)}\n" +
                          $"<b>Duration:</b> {TimeSpanUtilities.FormatDuration(duration)}\n" +
                          $"<b>Expires:</b> {expiresAt:yyyy-MM-dd HH:mm} UTC\n\n" +
                          $"You will be automatically unbanned after this time.";

        // Collect invite links for all active chats
        var inviteLinkSection = await BuildInviteLinkSectionAsync(activeChats, ct);
        if (!string.IsNullOrEmpty(inviteLinkSection))
        {
            notificationMessage += $"\n\n**Rejoin Links:**\n{inviteLinkSection}";
        }

        var notification = new Notification("tempban", notificationMessage);
        var result = await _notificationOrchestrator.SendTelegramDmAsync(userId, notification, ct);

        if (result.Success)
        {
            _logger.LogInformation(
                "Sent temp-ban notification to user {UserId} (expires: {ExpiresAt})",
                userId, expiresAt);
        }
        else
        {
            _logger.LogWarning(
                "Failed to send temp-ban notification to user {UserId}: {Error}",
                userId, result.ErrorMessage ?? "Unknown error");
        }

        return result.Success;
    }

    /// <summary>
    /// Send ban notification to admins subscribed to UserBanned events.
    /// </summary>
    private async Task SendBanAdminNotificationAsync(long userId, Actor executor, string? reason, CancellationToken ct)
    {
        // Get user info for the notification
        var userInfo = await GetUserDisplayNameAsync(userId, ct);

        var subject = "User Banned";
        var message = $"User {userInfo} has been banned.\n\n" +
                      $"Reason: {reason}\n" +
                      $"Banned by: {executor.GetDisplayText()}";

        // Send as a system notification to owners
        await _notificationService.SendSystemNotificationAsync(
            NotificationEventType.UserBanned,
            subject,
            message,
            ct);

        _logger.LogDebug(
            "Dispatched UserBanned admin notification for user {UserId}",
            userId);
    }

    /// <summary>
    /// Get display name for a Telegram user.
    /// </summary>
    private async Task<string> GetUserDisplayNameAsync(long userId, CancellationToken ct)
    {
        try
        {
            var user = await _telegramUserRepository.GetByTelegramIdAsync(userId, ct);
            if (user != null)
            {
                return TelegramDisplayName.Format(user.FirstName, user.LastName, user.Username, userId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get user display name for {UserId}", userId);
        }

        return $"User {userId}";
    }

    /// <summary>
    /// Build invite link section for rejoin notifications.
    /// </summary>
    private async Task<string> BuildInviteLinkSectionAsync(
        List<ManagedChatRecord> activeChats,
        CancellationToken ct)
    {
        var inviteLinks = new List<string>();

        foreach (var chat in activeChats)
        {
            try
            {
                var inviteLink = await _chatInviteLinkService.GetInviteLinkAsync(chat.ChatId, ct);
                if (!string.IsNullOrEmpty(inviteLink))
                {
                    var chatName = chat.ChatName ?? $"Chat {chat.ChatId}";
                    inviteLinks.Add($"• [{chatName}]({inviteLink})");
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "Failed to get invite link for chat {ChatId}, skipping from notification",
                    chat.ChatId);
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
