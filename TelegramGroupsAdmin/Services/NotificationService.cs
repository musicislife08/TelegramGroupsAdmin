using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Services.Email;
using TelegramGroupsAdmin.Telegram.Constants;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.Services;

/// <summary>
/// Service for sending notifications to users through configured channels
/// Hybrid approach: chat-specific events notify chat admins, system events notify Owners only
/// Uses Channel×Event matrix for per-channel, per-event configuration
/// </summary>
public class NotificationService : INotificationService
{
    private readonly INotificationPreferencesRepository _preferencesRepo;
    private readonly IEmailService _emailService;
    private readonly IDmDeliveryService _dmDeliveryService;
    private readonly IWebPushNotificationService _webPushService;
    private readonly ITelegramUserMappingRepository _telegramMappingRepo;
    private readonly IChatAdminsRepository _chatAdminsRepo;
    private readonly IManagedChatsRepository _managedChatsRepo;
    private readonly IUserRepository _userRepo;
    private readonly IReportCallbackContextRepository _callbackContextRepo;
    private readonly ILogger<NotificationService> _logger;
    private DateTime _lastDeliveryFailureLog = DateTime.MinValue;

    public NotificationService(
        INotificationPreferencesRepository preferencesRepo,
        IEmailService emailService,
        IDmDeliveryService dmDeliveryService,
        IWebPushNotificationService webPushService,
        ITelegramUserMappingRepository telegramMappingRepo,
        IChatAdminsRepository chatAdminsRepo,
        IManagedChatsRepository managedChatsRepo,
        IUserRepository userRepo,
        IReportCallbackContextRepository callbackContextRepo,
        ILogger<NotificationService> logger)
    {
        _preferencesRepo = preferencesRepo;
        _emailService = emailService;
        _dmDeliveryService = dmDeliveryService;
        _webPushService = webPushService;
        _telegramMappingRepo = telegramMappingRepo;
        _chatAdminsRepo = chatAdminsRepo;
        _managedChatsRepo = managedChatsRepo;
        _userRepo = userRepo;
        _callbackContextRepo = callbackContextRepo;
        _logger = logger;
    }

    public async Task<Dictionary<string, bool>> SendChatNotificationAsync(
        long chatId,
        NotificationEventType eventType,
        string subject,
        string message,
        long? reportId = null,
        string? photoPath = null,
        long? reportedUserId = null,
        CancellationToken cancellationToken = default)
    {
        // Fetch chat once for logging (reuse for all logs in this method)
        var chat = await _managedChatsRepo.GetByChatIdAsync(chatId, cancellationToken);

        try
        {
            // Get all active admins for this chat (includes LinkedWebUser via JOIN)
            var chatAdmins = await _chatAdminsRepo.GetChatAdminsAsync(chatId, cancellationToken);

            if (chatAdmins.Count == 0)
            {
                _logger.LogWarning("No admins found for {Chat}, cannot send notification",
                    chat.ToLogDebug(chatId));
                return new Dictionary<string, bool>();
            }

            // Filter to admins with linked web accounts
            var linkedAdmins = chatAdmins
                .Where(a => a.LinkedWebUser != null)
                .Select(a => a.LinkedWebUser!)
                .ToList();

            if (linkedAdmins.Count == 0)
            {
                _logger.LogWarning(
                    "{Chat} has {AdminCount} admins, but none are linked to web accounts",
                    chat.ToLogDebug(chatId), chatAdmins.Count);
                return new Dictionary<string, bool>();
            }

            _logger.LogInformation(
                "Sending chat notification for {Chat} to {UserCount} linked admin(s)",
                chat.ToLogInfo(chatId), linkedAdmins.Count);

            // Send notifications to all linked admins
            var results = new Dictionary<string, bool>();
            foreach (var user in linkedAdmins)
            {
                var success = await SendNotificationAsync(
                    user, eventType, subject, message,
                    reportId, photoPath, reportedUserId, chatId,
                    cancellationToken);
                results[user.Id] = success;
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send chat notification for {Chat}, event {EventType}",
                chat.ToLogDebug(chatId), eventType);
            return new Dictionary<string, bool>();
        }
    }

    public async Task<Dictionary<string, bool>> SendSystemNotificationAsync(
        NotificationEventType eventType,
        string subject,
        string message,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get all users with Owner permission level (PermissionLevel = 2)
            var allUsers = await _userRepo.GetAllAsync(cancellationToken);
            var ownerUsers = allUsers.Where(u => u.PermissionLevel == PermissionLevel.Owner).ToList();

            if (ownerUsers.Count == 0)
            {
                _logger.LogWarning("No Owner users found in system, cannot send system notification");
                return new Dictionary<string, bool>();
            }

            _logger.LogInformation(
                "Sending system notification to {OwnerCount} Owner user(s) for event {EventType}",
                ownerUsers.Count, eventType);

            // Send notifications to all Owners
            var results = new Dictionary<string, bool>();
            foreach (var owner in ownerUsers)
            {
                var success = await SendNotificationAsync(owner, eventType, subject, message, cancellationToken);
                results[owner.Id] = success;
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send system notification for event {EventType}", eventType);
            return new Dictionary<string, bool>();
        }
    }

    public async Task<bool> SendNotificationAsync(
        UserRecord user,
        NotificationEventType eventType,
        string subject,
        string message,
        CancellationToken cancellationToken = default)
    {
        // Call internal method without report-specific parameters
        return await SendNotificationAsync(
            user, eventType, subject, message,
            reportId: null, photoPath: null, reportedUserId: null, chatId: null,
            cancellationToken);
    }

    private async Task<bool> SendNotificationAsync(
        UserRecord user,
        NotificationEventType eventType,
        string subject,
        string message,
        long? reportId,
        string? photoPath,
        long? reportedUserId,
        long? chatId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get user preferences (creates default if not exists)
            var config = await _preferencesRepo.GetOrCreateAsync(user.Id, cancellationToken);

            var deliverySuccess = false;

            // Telegram DM channel - check if event is enabled for this channel
            if (config.IsEnabled(NotificationChannel.TelegramDm, eventType))
            {
                var telegramSuccess = await SendTelegramDmAsync(
                    user.Id, subject, message,
                    reportId, photoPath, reportedUserId, chatId,
                    cancellationToken);
                deliverySuccess = deliverySuccess || telegramSuccess;
            }

            // Email channel - check if event is enabled for this channel
            if (config.IsEnabled(NotificationChannel.Email, eventType))
            {
                var emailSuccess = await SendEmailAsync(user, subject, message, cancellationToken);
                deliverySuccess = deliverySuccess || emailSuccess;
            }

            // WebPush channel (in-app notifications) - check if event is enabled for this channel
            if (config.IsEnabled(NotificationChannel.WebPush, eventType))
            {
                var webPushSuccess = await _webPushService.SendAsync(user, eventType, subject, message, cancellationToken);
                deliverySuccess = deliverySuccess || webPushSuccess;
            }

            if (!deliverySuccess)
            {
                // Check if user has any channels configured for this event
                var hasTelegramEnabled = config.IsEnabled(NotificationChannel.TelegramDm, eventType);
                var hasEmailEnabled = config.IsEnabled(NotificationChannel.Email, eventType);
                var hasWebPushEnabled = config.IsEnabled(NotificationChannel.WebPush, eventType);

                if (!hasTelegramEnabled && !hasEmailEnabled && !hasWebPushEnabled)
                {
                    _logger.LogDebug("{User} has no channels enabled for event type {EventType}",
                        user.ToLogDebug(), eventType);
                }
                else
                {
                    // Throttle logging to once per minute to avoid spam during network outages
                    var now = DateTime.UtcNow;
                    if ((now - _lastDeliveryFailureLog).TotalSeconds >= 60)
                    {
                        _logger.LogWarning(
                            "Failed to deliver notification to {User} via any enabled channel",
                            user.ToLogDebug());
                        _lastDeliveryFailureLog = now;
                    }
                }
            }

            return deliverySuccess;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send notification to {User} for event {EventType}",
                user.ToLogDebug(), eventType);
            return false;
        }
    }

    /// <summary>
    /// Send notification via Telegram DM
    /// Maps web user ID to Telegram ID and delivers via DM (with queue fallback)
    /// </summary>
    private async Task<bool> SendTelegramDmAsync(
        string userId,
        string subject,
        string message,
        long? reportId,
        string? photoPath,
        long? reportedUserId,
        long? chatId,
        CancellationToken cancellationToken)
    {
        // Fetch once for logging
        var user = await _userRepo.GetByIdAsync(userId, cancellationToken);

        try
        {
            // Get Telegram mapping for this web user
            var mappings = await _telegramMappingRepo.GetByUserIdAsync(userId, cancellationToken);
            var mapping = mappings.FirstOrDefault();

            if (mapping == null)
            {
                _logger.LogDebug("User {User} has no Telegram account linked, skipping Telegram DM",
                    user.ToLogDebug(userId));
                return false;
            }

            // Format message with subject
            var formattedMessage = $"🔔 *{EscapeMarkdown(subject)}*\n\n{EscapeMarkdown(message)}";

            // Build inline keyboard if this is a report notification with action context
            InlineKeyboardMarkup? keyboard = null;
            if (reportId.HasValue && reportedUserId.HasValue && chatId.HasValue)
            {
                keyboard = await BuildReportActionKeyboardAsync(reportId.Value, chatId.Value, reportedUserId.Value, cancellationToken);
            }

            DmDeliveryResult result;

            // Send with photo and keyboard if we have report context
            if (keyboard != null || !string.IsNullOrWhiteSpace(photoPath))
            {
                result = await _dmDeliveryService.SendDmWithMediaAndKeyboardAsync(
                    mapping.TelegramId,
                    "report",
                    formattedMessage,
                    photoPath,
                    keyboard,
                    cancellationToken);
            }
            else
            {
                // Standard DM with queue fallback
                result = await _dmDeliveryService.SendDmWithQueueAsync(
                    mapping.TelegramId,
                    "notification",
                    formattedMessage,
                    cancellationToken);
            }

            if (result.DmSent)
            {
                _logger.LogInformation("Sent Telegram DM notification to {User} (Telegram ID {TelegramId})",
                    user.ToLogInfo(userId), mapping.TelegramId);
                return true;
            }

            if (!result.Failed)
            {
                // Queued for later delivery
                _logger.LogDebug("Telegram DM queued for {User} (Telegram ID {TelegramId})",
                    user.ToLogDebug(userId), mapping.TelegramId);
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Telegram DM to {User}", user.ToLogDebug(userId));
            return false;
        }
    }

    /// <summary>
    /// Build inline keyboard with report moderation action buttons.
    /// Creates ONE database-backed callback context per report (short IDs, no 64-byte limit issues).
    /// </summary>
    private async Task<InlineKeyboardMarkup> BuildReportActionKeyboardAsync(
        long reportId,
        long chatId,
        long userId,
        CancellationToken cancellationToken)
    {
        // Create ONE context for this report - action is passed in callback data
        var context = new ReportCallbackContext(
            Id: 0, // Generated by database
            ReportId: reportId,
            ChatId: chatId,
            UserId: userId,
            CreatedAt: DateTimeOffset.UtcNow);
        var contextId = await _callbackContextRepo.CreateAsync(context, cancellationToken);

        // Format: rpt:{contextId}:{action} - always under 20 bytes!
        return new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🚫 Spam", $"rpt:{contextId}:{(int)ReportAction.Spam}"),
                InlineKeyboardButton.WithCallbackData("⚠️ Warn", $"rpt:{contextId}:{(int)ReportAction.Warn}")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("⏱️ TempBan", $"rpt:{contextId}:{(int)ReportAction.TempBan}"),
                InlineKeyboardButton.WithCallbackData("✓ Dismiss", $"rpt:{contextId}:{(int)ReportAction.Dismiss}")
            }
        });
    }

    /// <summary>
    /// Send notification via Email
    /// Uses user's account email (validated)
    /// </summary>
    private async Task<bool> SendEmailAsync(
        UserRecord user,
        string subject,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            var emailAddress = user.Email;

            // Format message as HTML email
            var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 20px auto; padding: 20px; border: 1px solid #ddd; border-radius: 5px; }}
        h2 {{ color: #2c3e50; border-bottom: 2px solid #3498db; padding-bottom: 10px; }}
        .footer {{ margin-top: 30px; padding-top: 20px; border-top: 1px solid #ddd; font-size: 12px; color: #666; }}
    </style>
</head>
<body>
    <div class=""container"">
        <h2>{subject}</h2>
        <p>{message.Replace("\n", "<br>")}</p>
        <div class=""footer"">
            <p>This is an automated notification from TelegramGroupsAdmin.</p>
            <p>To manage your notification preferences, visit your <a href=""#"">Profile Settings</a>.</p>
        </div>
    </div>
</body>
</html>";

            await _emailService.SendEmailAsync(emailAddress, subject, htmlBody, isHtml: true, cancellationToken);

            _logger.LogInformation("Sent email notification to {User}",
                user.ToLogInfo());

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {User}",
                user.ToLogDebug());
            return false;
        }
    }

    /// <summary>
    /// Escape special Markdown characters for Telegram messages
    /// </summary>
    private static string EscapeMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Escape Markdown special characters for Telegram MarkdownV2
        return text
            .Replace("_", "\\_")
            .Replace("*", "\\*")
            .Replace("[", "\\[")
            .Replace("]", "\\]")
            .Replace("(", "\\(")
            .Replace(")", "\\)")
            .Replace("~", "\\~")
            .Replace("`", "\\`")
            .Replace(">", "\\>")
            .Replace("#", "\\#")
            .Replace("+", "\\+")
            .Replace("-", "\\-")
            .Replace("=", "\\=")
            .Replace("|", "\\|")
            .Replace("{", "\\{")
            .Replace("}", "\\}")
            .Replace(".", "\\.")
            .Replace("!", "\\!");
    }
}
