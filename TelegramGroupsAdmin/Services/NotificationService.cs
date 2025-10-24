using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Services.Email;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Core.Services;
using CoreModels = TelegramGroupsAdmin.Core.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Services;

/// <summary>
/// Service for sending notifications to users through configured channels
/// Hybrid approach: chat-specific events notify chat admins, system events notify Owners only
/// Phase 5.1: Initial implementation with Telegram DM and Email channels
/// </summary>
public class NotificationService : INotificationService
{
    private readonly INotificationPreferencesRepository _preferencesRepo;
    private readonly IEmailService _emailService;
    private readonly IDmDeliveryService _dmDeliveryService;
    private readonly ITelegramUserMappingRepository _telegramMappingRepo;
    private readonly IChatAdminsRepository _chatAdminsRepo;
    private readonly IUserRepository _userRepo;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        INotificationPreferencesRepository preferencesRepo,
        IEmailService emailService,
        IDmDeliveryService dmDeliveryService,
        ITelegramUserMappingRepository telegramMappingRepo,
        IChatAdminsRepository chatAdminsRepo,
        IUserRepository userRepo,
        ILogger<NotificationService> logger)
    {
        _preferencesRepo = preferencesRepo;
        _emailService = emailService;
        _dmDeliveryService = dmDeliveryService;
        _telegramMappingRepo = telegramMappingRepo;
        _chatAdminsRepo = chatAdminsRepo;
        _userRepo = userRepo;
        _logger = logger;
    }

    public async Task<Dictionary<string, bool>> SendChatNotificationAsync(
        long chatId,
        CoreModels.NotificationEventType eventType,
        string subject,
        string message,
        CancellationToken ct = default)
    {
        try
        {
            // Get all active admins for this chat (telegram IDs)
            var chatAdmins = await _chatAdminsRepo.GetChatAdminsAsync(chatId, ct);

            if (!chatAdmins.Any())
            {
                _logger.LogWarning("No admins found for chat {ChatId}, cannot send notification", chatId);
                return new Dictionary<string, bool>();
            }

            // Map telegram IDs to web user IDs
            var webUserIds = new List<string>();

            foreach (var admin in chatAdmins)
            {
                var mapping = await _telegramMappingRepo.GetByTelegramIdAsync(admin.TelegramId, ct);
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
                var success = await SendNotificationAsync(userId, eventType, subject, message, ct);
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
        CoreModels.NotificationEventType eventType,
        string subject,
        string message,
        CancellationToken ct = default)
    {
        try
        {
            // Get all users with Owner permission level (PermissionLevel = 2)
            var allUsers = await _userRepo.GetAllAsync(ct);
            var ownerUsers = allUsers.Where(u => u.PermissionLevel == CoreModels.PermissionLevel.Owner).ToList();

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
                var success = await SendNotificationAsync(owner.Id, eventType, subject, message, ct);
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
        CoreModels.NotificationEventType eventType,
        string subject,
        string message,
        CancellationToken ct = default)
    {
        try
        {
            // Get user preferences (creates default if not exists)
            var preferences = await _preferencesRepo.GetOrCreatePreferencesAsync(userId, ct);

            // Check if user wants notifications for this event type
            if (!preferences.EventFilters.IsEnabled(eventType))
            {
                _logger.LogDebug("User {UserId} has disabled notifications for event type {EventType}",
                    userId, eventType);
                return false;
            }

            var deliverySuccess = false;

            // Telegram DM channel
            if (preferences.TelegramDmEnabled)
            {
                var telegramSuccess = await SendTelegramDmAsync(userId, subject, message, ct);
                deliverySuccess = deliverySuccess || telegramSuccess;
            }

            // Email channel
            if (preferences.EmailEnabled)
            {
                var emailSuccess = await SendEmailAsync(userId, preferences, subject, message, ct);
                deliverySuccess = deliverySuccess || emailSuccess;
            }

            if (!deliverySuccess)
            {
                _logger.LogWarning(
                    "Failed to deliver notification to user {UserId} via any enabled channel (TelegramDM={TelegramEnabled}, Email={EmailEnabled})",
                    userId, preferences.TelegramDmEnabled, preferences.EmailEnabled);
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
        CoreModels.NotificationEventType eventType,
        string subject,
        string message,
        CancellationToken ct = default)
    {
        var results = new Dictionary<string, bool>();

        // Send notifications in parallel for better performance
        var tasks = userIds.Select(async userId =>
        {
            var success = await SendNotificationAsync(userId, eventType, subject, message, ct);
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
    private async Task<bool> SendTelegramDmAsync(string userId, string subject, string message, CancellationToken ct)
    {
        try
        {
            // Get Telegram mapping for this web user
            var mappings = await _telegramMappingRepo.GetByUserIdAsync(userId, ct);
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
                ct);

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
    /// Uses user's configured email address (or account email if not specified)
    /// </summary>
    private async Task<bool> SendEmailAsync(
        string userId,
        Telegram.Models.NotificationPreferences preferences,
        string subject,
        string message,
        CancellationToken ct)
    {
        try
        {
            // Determine email address (custom or account email)
            var emailAddress = preferences.ChannelConfigs.Email?.Address;

            if (string.IsNullOrWhiteSpace(emailAddress))
            {
                // Use account email
                var user = await _userRepo.GetByIdAsync(userId, ct);
                if (user == null)
                {
                    _logger.LogWarning("User {UserId} not found, cannot send email notification", userId);
                    return false;
                }

                emailAddress = user.Email;
            }

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

            await _emailService.SendEmailAsync(emailAddress, subject, htmlBody, isHtml: true, ct);

            _logger.LogInformation("Sent email notification to user {UserId} at {EmailAddress}",
                userId, emailAddress);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to user {UserId}", userId);
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
