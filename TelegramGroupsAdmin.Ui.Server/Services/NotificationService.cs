using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Ui.Server.Repositories;
using TelegramGroupsAdmin.Ui.Server.Services.Email;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.Ui.Server.Services;

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
    private readonly IUserRepository _userRepo;
    private readonly ILogger<NotificationService> _logger;
    private DateTime _lastDeliveryFailureLog = DateTime.MinValue;

    public NotificationService(
        INotificationPreferencesRepository preferencesRepo,
        IEmailService emailService,
        IDmDeliveryService dmDeliveryService,
        IWebPushNotificationService webPushService,
        ITelegramUserMappingRepository telegramMappingRepo,
        IChatAdminsRepository chatAdminsRepo,
        IUserRepository userRepo,
        ILogger<NotificationService> logger)
    {
        _preferencesRepo = preferencesRepo;
        _emailService = emailService;
        _dmDeliveryService = dmDeliveryService;
        _webPushService = webPushService;
        _telegramMappingRepo = telegramMappingRepo;
        _chatAdminsRepo = chatAdminsRepo;
        _userRepo = userRepo;
        _logger = logger;
    }

    public async Task<Dictionary<string, bool>> SendChatNotificationAsync(
        long chatId,
        NotificationEventType eventType,
        string subject,
        string message,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get all active admins for this chat (telegram IDs)
            var chatAdmins = await _chatAdminsRepo.GetChatAdminsAsync(chatId, cancellationToken);

            if (!chatAdmins.Any())
            {
                _logger.LogWarning("No admins found for chat {ChatId}, cannot send notification", chatId);
                return new Dictionary<string, bool>();
            }

            // Map telegram IDs to web user IDs
            var webUserIds = new List<string>();

            foreach (var admin in chatAdmins)
            {
                var mapping = await _telegramMappingRepo.GetByTelegramIdAsync(admin.TelegramId, cancellationToken);
                if (mapping != null)
                {
                    webUserIds.Add(mapping.UserId);
                }
            }

            if (!webUserIds.Any())
            {
                _logger.LogWarning(
                    "Chat {ChatId} has {AdminCount} admins, but none are linked to web accounts",
                    chatId, chatAdmins.Count);
                return new Dictionary<string, bool>();
            }

            _logger.LogInformation(
                "Sending chat notification for chat {ChatId} to {UserCount} linked admin(s)",
                chatId, webUserIds.Count);

            // Send notifications to all linked admins
            var results = new Dictionary<string, bool>();
            foreach (var userId in webUserIds)
            {
                var success = await SendNotificationAsync(userId, eventType, subject, message, cancellationToken);
                results[userId] = success;
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send chat notification for chat {ChatId}, event {EventType}",
                chatId, eventType);
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

            if (!ownerUsers.Any())
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
                var success = await SendNotificationAsync(owner.Id, eventType, subject, message, cancellationToken);
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
        string userId,
        NotificationEventType eventType,
        string subject,
        string message,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get user preferences (creates default if not exists)
            var config = await _preferencesRepo.GetOrCreateAsync(userId, cancellationToken);

            var deliverySuccess = false;

            // Telegram DM channel - check if event is enabled for this channel
            if (config.IsEnabled(NotificationChannel.TelegramDm, eventType))
            {
                var telegramSuccess = await SendTelegramDmAsync(userId, subject, message, cancellationToken);
                deliverySuccess = deliverySuccess || telegramSuccess;
            }

            // Email channel - check if event is enabled for this channel
            if (config.IsEnabled(NotificationChannel.Email, eventType))
            {
                var emailSuccess = await SendEmailAsync(userId, subject, message, cancellationToken);
                deliverySuccess = deliverySuccess || emailSuccess;
            }

            // WebPush channel (in-app notifications) - check if event is enabled for this channel
            if (config.IsEnabled(NotificationChannel.WebPush, eventType))
            {
                var webPushSuccess = await _webPushService.SendAsync(userId, eventType, subject, message, cancellationToken);
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
                    _logger.LogDebug("User {UserId} has no channels enabled for event type {EventType}",
                        userId, eventType);
                }
                else
                {
                    // Throttle logging to once per minute to avoid spam during network outages
                    var now = DateTime.UtcNow;
                    if ((now - _lastDeliveryFailureLog).TotalSeconds >= 60)
                    {
                        _logger.LogWarning(
                            "Failed to deliver notification to user {UserId} via any enabled channel",
                            userId);
                        _lastDeliveryFailureLog = now;
                    }
                }
            }

            return deliverySuccess;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send notification to user {UserId} for event {EventType}",
                userId, eventType);
            return false;
        }
    }

    public async Task<Dictionary<string, bool>> SendNotificationToMultipleAsync(
        IEnumerable<string> userIds,
        NotificationEventType eventType,
        string subject,
        string message,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, bool>();

        // Send notifications in parallel for better performance
        var tasks = userIds.Select(async userId =>
        {
            var success = await SendNotificationAsync(userId, eventType, subject, message, cancellationToken);
            return (UserId: userId, Success: success);
        });

        var completedTasks = await Task.WhenAll(tasks);

        foreach (var (userId, success) in completedTasks)
        {
            results[userId] = success;
        }

        return results;
    }

    /// <summary>
    /// Send notification via Telegram DM
    /// Maps web user ID to Telegram ID and delivers via DM (with queue fallback)
    /// </summary>
    private async Task<bool> SendTelegramDmAsync(string userId, string subject, string message, CancellationToken cancellationToken)
    {
        try
        {
            // Get Telegram mapping for this web user
            var mappings = await _telegramMappingRepo.GetByUserIdAsync(userId, cancellationToken);
            var mapping = mappings.FirstOrDefault();

            if (mapping == null)
            {
                _logger.LogDebug("User {UserId} has no Telegram account linked, skipping Telegram DM", userId);
                return false;
            }

            // Format message with subject
            var formattedMessage = $"🔔 *{EscapeMarkdown(subject)}*\n\n{EscapeMarkdown(message)}";

            // Send DM with queue fallback (pending notifications)
            var result = await _dmDeliveryService.SendDmWithQueueAsync(
                mapping.TelegramId,
                "notification", // notification type for pending_notifications table
                formattedMessage,
                cancellationToken);

            if (result.DmSent)
            {
                _logger.LogInformation("Sent Telegram DM notification to user {UserId} (Telegram ID {TelegramId})",
                    userId, mapping.TelegramId);
                return true;
            }

            if (!result.Failed)
            {
                // Queued for later delivery
                _logger.LogDebug("Telegram DM queued for user {UserId} (Telegram ID {TelegramId})",
                    userId, mapping.TelegramId);
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Telegram DM to user {UserId}", userId);
            return false;
        }
    }

    /// <summary>
    /// Send notification via Email
    /// Uses user's account email (validated)
    /// </summary>
    private async Task<bool> SendEmailAsync(
        string userId,
        string subject,
        string message,
        CancellationToken cancellationToken)
    {
        Telegram.Models.UserRecord? user = null;
        try
        {
            // Get user's account email
            user = await _userRepo.GetByIdAsync(userId, cancellationToken);
            if (user == null)
            {
                _logger.LogWarning("User {UserId} not found, cannot send email notification", userId);
                return false;
            }

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
                LogDisplayName.WebUserInfo(emailAddress, userId));

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {User}",
                LogDisplayName.WebUserDebug(user?.Email, userId));
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
