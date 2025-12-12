using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Events;
using TelegramGroupsAdmin.Telegram.Services.Notifications;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Handlers;

/// <summary>
/// Domain expert for all moderation-related notifications.
/// Handles both user DM notifications and admin notifications via NotificationService.
///
/// User DM notifications:
/// - Warnings: Sent to user with warning count
/// - Temp bans: Sent to user with duration and rejoin links
/// - Bans: Future - will include appeal info when appeal system is implemented
///
/// Admin notifications:
/// - Bans: Sent to admins subscribed to UserBanned event type
/// - MarkAsSpamAndBan: Sent to admins subscribed to UserBanned event type
///
/// Order: 200 (runs last - notifications are non-critical side-effects)
/// </summary>
public class NotificationHandler : IModerationHandler
{
    private readonly INotificationOrchestrator _notificationOrchestrator;
    private readonly INotificationService _notificationService;
    private readonly IManagedChatsRepository _managedChatsRepository;
    private readonly ITelegramUserRepository _telegramUserRepository;
    private readonly IChatInviteLinkService _chatInviteLinkService;
    private readonly ILogger<NotificationHandler> _logger;

    public int Order => 200;

    public ModerationActionType[] AppliesTo =>
    [
        ModerationActionType.Warn,
        ModerationActionType.TempBan,
        ModerationActionType.Ban,
        ModerationActionType.MarkAsSpamAndBan
    ];

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

    public async Task<ModerationFollowUp> HandleAsync(ModerationEvent evt, CancellationToken ct = default)
    {
        try
        {
            switch (evt.ActionType)
            {
                case ModerationActionType.Warn:
                    await SendWarningNotificationAsync(evt, ct);
                    break;

                case ModerationActionType.TempBan:
                    await SendTempBanNotificationAsync(evt, ct);
                    break;

                case ModerationActionType.Ban:
                    await SendBanAdminNotificationAsync(evt, ct);
                    break;

                case ModerationActionType.MarkAsSpamAndBan:
                    await SendMarkAsSpamAdminNotificationAsync(evt, ct);
                    break;
            }
        }
        catch (Exception ex)
        {
            // Notification failures are non-critical - log but don't fail the action
            _logger.LogWarning(ex,
                "Failed to send notification for user {UserId} ({ActionType}). " +
                "This is non-critical - the moderation action succeeded.",
                evt.UserId, evt.ActionType);
        }

        return ModerationFollowUp.None;
    }

    /// <summary>
    /// Send warning DM to the user.
    /// </summary>
    private async Task SendWarningNotificationAsync(ModerationEvent evt, CancellationToken ct)
    {
        var message = $"⚠️ **Warning Issued**\n\n" +
                      $"You have received a warning.\n\n" +
                      $"**Reason:** {evt.Reason}\n" +
                      $"**Total Warnings:** {evt.WarningCount}\n\n" +
                      $"Please review the group rules and avoid similar behavior in the future.\n\n" +
                      $"💡 Use /mystatus to check your current status.";

        var notification = new Notification("warning", message);
        var result = await _notificationOrchestrator.SendTelegramDmAsync(evt.UserId, notification, ct);

        if (result.Success)
        {
            _logger.LogInformation(
                "Sent warning notification to user {UserId} (warning #{Count})",
                evt.UserId, evt.WarningCount);
        }
        else
        {
            _logger.LogWarning(
                "Failed to send warning notification to user {UserId}: {Error}",
                evt.UserId, result.ErrorMessage ?? "Unknown error");
        }
    }

    /// <summary>
    /// Send temp ban DM to the user with rejoin links.
    /// </summary>
    private async Task SendTempBanNotificationAsync(ModerationEvent evt, CancellationToken ct)
    {
        if (!evt.Duration.HasValue || !evt.ExpiresAt.HasValue)
        {
            _logger.LogWarning(
                "TempBan event for user {UserId} missing duration or expiry, skipping notification",
                evt.UserId);
            return;
        }

        // Get all active managed chats for rejoin links
        var allChats = await _managedChatsRepository.GetAllChatsAsync(ct);
        var activeChats = allChats.Where(c => c.IsActive && !c.IsDeleted).ToList();

        // Build notification message
        var notification = $"⏱️ **You have been temporarily banned**\n\n" +
                          $"**Reason:** {evt.Reason}\n" +
                          $"**Duration:** {TimeSpanUtilities.FormatDuration(evt.Duration.Value)}\n" +
                          $"**Expires:** {evt.ExpiresAt.Value:yyyy-MM-dd HH:mm} UTC\n\n" +
                          $"You will be automatically unbanned after this time.";

        // Collect invite links for all active chats
        var inviteLinkSection = await BuildInviteLinkSectionAsync(activeChats, ct);
        if (!string.IsNullOrEmpty(inviteLinkSection))
        {
            notification += $"\n\n**Rejoin Links:**\n{inviteLinkSection}";
        }

        var notificationObj = new Notification("tempban", notification);
        var result = await _notificationOrchestrator.SendTelegramDmAsync(evt.UserId, notificationObj, ct);

        if (result.Success)
        {
            _logger.LogInformation(
                "Sent temp-ban notification to user {UserId} (expires: {ExpiresAt})",
                evt.UserId, evt.ExpiresAt.Value);
        }
        else
        {
            _logger.LogWarning(
                "Failed to send temp-ban notification to user {UserId}: {Error}",
                evt.UserId, result.ErrorMessage ?? "Unknown error");
        }
    }

    /// <summary>
    /// Send ban notification to admins subscribed to UserBanned events.
    /// Note: User DM notification for bans will be added with appeal system implementation.
    /// </summary>
    private async Task SendBanAdminNotificationAsync(ModerationEvent evt, CancellationToken ct)
    {
        // Get user info for the notification
        var userInfo = await GetUserDisplayNameAsync(evt.UserId, ct);

        var subject = "User Banned";
        var message = $"User {userInfo} has been banned.\n\n" +
                      $"Reason: {evt.Reason}\n" +
                      $"Chats affected: {evt.ChatsAffected}\n" +
                      $"Banned by: {evt.Executor.GetDisplayText()}\n" +
                      $"Trust revoked: {(evt.TrustRemoved ? "Yes" : "No")}";

        // Send to admins of all managed chats (those subscribed to UserBanned)
        // If the ban was triggered from a specific chat context, we could use that
        // For now, send as a system notification to owners
        await _notificationService.SendSystemNotificationAsync(
            NotificationEventType.UserBanned,
            subject,
            message,
            ct);

        _logger.LogDebug(
            "Dispatched UserBanned admin notification for user {UserId}",
            evt.UserId);
    }

    /// <summary>
    /// Send mark as spam notification to admins subscribed to UserBanned events.
    /// </summary>
    private async Task SendMarkAsSpamAdminNotificationAsync(ModerationEvent evt, CancellationToken ct)
    {
        // Get user info for the notification
        var userInfo = await GetUserDisplayNameAsync(evt.UserId, ct);

        var subject = "User Marked as Spam & Banned";
        var message = $"User {userInfo} has been marked as spam and banned.\n\n" +
                      $"Reason: {evt.Reason}\n" +
                      $"Message deleted: {(evt.MessageDeleted ? "Yes" : "No")}\n" +
                      $"Chats affected: {evt.ChatsAffected}\n" +
                      $"Banned by: {evt.Executor.GetDisplayText()}\n" +
                      $"Trust revoked: {(evt.TrustRemoved ? "Yes" : "No")}";

        // If we have a chat context, send to that chat's admins
        if (evt.ChatId.HasValue)
        {
            await _notificationService.SendChatNotificationAsync(
                evt.ChatId.Value,
                NotificationEventType.UserBanned,
                subject,
                message,
                ct);
        }
        else
        {
            // No chat context - send as system notification
            await _notificationService.SendSystemNotificationAsync(
                NotificationEventType.UserBanned,
                subject,
                message,
                ct);
        }

        _logger.LogDebug(
            "Dispatched MarkAsSpam admin notification for user {UserId}",
            evt.UserId);
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
}
