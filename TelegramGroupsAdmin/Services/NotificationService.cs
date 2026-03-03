using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Services.Email;
using TelegramGroupsAdmin.Services.Notifications;
using TelegramGroupsAdmin.Telegram.Constants;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.Services;

/// <summary>
/// Notification service with typed intent-based methods.
/// Callers pass identity objects and raw domain values — this service owns all formatting
/// via NotificationRenderer and routes to the correct audience via two-pool routing.
/// </summary>
public sealed class NotificationService : INotificationService
{
    private readonly INotificationPreferencesRepository _preferencesRepo;
    private readonly IEmailService _emailService;
    private readonly IBotDmService _dmDeliveryService;
    private readonly IWebPushNotificationService _webPushService;
    private readonly ITelegramUserMappingRepository _telegramMappingRepo;
    private readonly IChatAdminsRepository _chatAdminsRepo;
    private readonly IUserRepository _userRepo;
    private readonly IReportCallbackContextRepository _callbackContextRepo;
    private readonly ILogger<NotificationService> _logger;
    private DateTime _lastDeliveryFailureLog = DateTime.MinValue;

    public NotificationService(
        INotificationPreferencesRepository preferencesRepo,
        IEmailService emailService,
        IBotDmService dmDeliveryService,
        IWebPushNotificationService webPushService,
        ITelegramUserMappingRepository telegramMappingRepo,
        IChatAdminsRepository chatAdminsRepo,
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
        _userRepo = userRepo;
        _callbackContextRepo = callbackContextRepo;
        _logger = logger;
    }

    // ════════════════════════════════════════════════════════════════════════════
    // Typed Intent-Based Methods
    // ════════════════════════════════════════════════════════════════════════════

    public Task<Dictionary<string, bool>> SendSpamBanNotificationAsync(
        ChatIdentity chat,
        UserIdentity user,
        Actor? bannedBy,
        int netConfidence,
        int confidence,
        string? detectionReason,
        int chatsAffected,
        bool messageDeleted,
        int messageId,
        string? messagePreview,
        string? photoPath,
        string? videoPath,
        CancellationToken ct = default)
    {
        var title = bannedBy?.GetDisplayText() is { } name
            ? $"Spam Banned by {name}" : "Spam Auto-Banned";

        var payload = NotificationPayloadBuilder.Create(title)
            .WithField("User", user.DisplayName, telegramUserId: user.Id)
            .WithField("Chat", chat.ChatName ?? chat.Id.ToString())
            .WithSection("Message", s => s
                .WithText(messagePreview ?? "[No text]"))
            .WithSection("Detection", s => s
                .WithField("Net Confidence", $"{netConfidence}%")
                .WithField("Confidence", $"{confidence}%")
                .WithFieldIf(detectionReason != null, "Reason", detectionReason))
            .WithSection("Action Taken", s => s
                .WithField("Banned from", $"{chatsAffected} managed chats")
                .WithFieldIf(messageDeleted, "Message deleted", $"ID: {messageId}"))
            .WithPhoto(photoPath)
            .WithVideo(videoPath)
            .Build();

        return SendToChatAudienceAsync(chat, NotificationEventType.UserBanned, payload, ct);
    }

    public Task<Dictionary<string, bool>> SendReportNotificationAsync(
        ChatIdentity chat,
        UserIdentity? reportedUser,
        long? reporterUserId,
        string? reporterName,
        bool isAutomated,
        string messagePreview,
        string? photoPath,
        long reportId,
        ReportType reportType,
        CancellationToken ct = default)
    {
        var reporterDisplay = isAutomated ? "System (automated)" : reporterName ?? "Unknown";

        var payload = NotificationPayloadBuilder.Create("Message Reported")
            .WithField("Chat", chat.ChatName ?? chat.Id.ToString())
            .WithFieldIf(reportedUser != null, "Reported user", reportedUser?.DisplayName ?? "Unknown", telegramUserId: reportedUser?.Id)
            .WithField("Reported by", reporterDisplay, telegramUserId: isAutomated ? null : reporterUserId)
            .WithSection("Message", s => s
                .WithText(messagePreview))
            .WithPhoto(photoPath)
            .WithKeyboard(new ActionKeyboardContext(reportId, chat.Id, reportedUser?.Id ?? 0, reportType))
            .Build();

        return SendToChatAudienceAsync(chat, NotificationEventType.MessageReported, payload, ct);
    }

    public Task<Dictionary<string, bool>> SendProfileScanAlertAsync(
        ChatIdentity chat,
        UserIdentity user,
        decimal score,
        string signals,
        string? aiReason,
        long reportId,
        CancellationToken ct = default)
    {
        var payload = NotificationPayloadBuilder.Create("Profile Scan Alert")
            .WithField("User", user.DisplayName, telegramUserId: user.Id)
            .WithField("Chat", chat.ChatName ?? chat.Id.ToString())
            .WithSection("Analysis", s => s
                .WithField("Score", $"{score:F1}")
                .WithField("Signals", signals)
                .WithFieldIf(aiReason != null, "AI Reasoning", aiReason))
            .WithKeyboard(new ActionKeyboardContext(reportId, chat.Id, user.Id, ReportType.ProfileScanAlert))
            .Build();

        return SendToChatAudienceAsync(chat, NotificationEventType.ProfileScanAlert, payload, ct);
    }

    public Task<Dictionary<string, bool>> SendExamFailureNotificationAsync(
        ChatIdentity chat,
        UserIdentity user,
        int mcCorrectCount,
        int mcTotal,
        int mcScore,
        int mcPassingThreshold,
        string? openEndedQuestion,
        string? openEndedAnswer,
        string? aiReasoning,
        long examFailureId,
        CancellationToken ct = default)
    {
        var payload = NotificationPayloadBuilder.Create("Entrance Exam Review Required")
            .WithField("User", user.DisplayName, telegramUserId: user.Id)
            .WithField("Chat", chat.ChatName ?? chat.Id.ToString())
            .WithSection("Results", s => s
                .WithField("Answered", $"{mcCorrectCount}/{mcTotal} correct")
                .WithField("Score", $"{mcScore}% (Required: {mcPassingThreshold}%)"))
            .WithSection("Open-Ended Response", s => s
                .WithFieldIf(openEndedQuestion != null, "Question", openEndedQuestion)
                .WithFieldIf(openEndedAnswer != null, "Answer", openEndedAnswer)
                .WithFieldIf(aiReasoning != null, "AI Reasoning", aiReasoning))
            .WithKeyboard(new ActionKeyboardContext(examFailureId, chat.Id, user.Id, ReportType.ExamFailure))
            .Build();

        return SendToChatAudienceAsync(chat, NotificationEventType.ExamFailed, payload, ct);
    }

    public Task<Dictionary<string, bool>> SendBanNotificationAsync(
        UserIdentity user,
        Actor executor,
        string? reason,
        ChatIdentity? chat = null,
        CancellationToken ct = default)
    {
        var payload = NotificationPayloadBuilder.Create($"User Banned: {user.DisplayName}")
            .WithField("User", user.DisplayName, telegramUserId: user.Id)
            .WithText(chat != null
                ? $"Banned from {chat.ChatName ?? chat.Id.ToString()}"
                : "Banned globally")
            .WithSection("Details", s => s
                .WithField("Reason", reason ?? "No reason provided")
                .WithField("Banned by", executor.GetDisplayText()))
            .Build();

        return SendToChatAudienceAsync(chat, NotificationEventType.UserBanned, payload, ct);
    }

    public Task<Dictionary<string, bool>> SendMalwareDetectedAsync(
        ChatIdentity chat,
        UserIdentity user,
        string malwareDetails,
        CancellationToken ct = default)
    {
        var payload = NotificationPayloadBuilder.Create("Malware Detected")
            .WithField("User", user.DisplayName, telegramUserId: user.Id)
            .WithField("Chat", chat.ChatName ?? chat.Id.ToString())
            .WithSection("Details", s => s
                .WithText(malwareDetails))
            .Build();

        return SendToChatAudienceAsync(chat, NotificationEventType.MalwareDetected, payload, ct);
    }

    public Task<Dictionary<string, bool>> SendAdminChangedAsync(
        ChatIdentity chat,
        UserIdentity user,
        bool promoted,
        bool isCreator,
        CancellationToken ct = default)
    {
        var action = promoted ? "Promoted" : "Demoted";
        var role = isCreator ? "creator" : "admin";

        var payload = NotificationPayloadBuilder.Create($"Admin {action}: {user.DisplayName}")
            .WithField("User", user.DisplayName, telegramUserId: user.Id)
            .WithField("Chat", chat.ChatName ?? chat.Id.ToString())
            .WithField("Action", $"{action} as {role}")
            .Build();

        return SendToChatAudienceAsync(chat, NotificationEventType.ChatAdminChanged, payload, ct);
    }

    public Task<Dictionary<string, bool>> SendBackupFailedAsync(
        string tableName,
        string error,
        CancellationToken ct = default)
    {
        var payload = NotificationPayloadBuilder.Create("Database Backup Failed")
            .WithText($"Backup failed while exporting table '{tableName}'")
            .WithSection("Error Details", s => s
                .WithField("Error", error))
            .Build();

        return SendToOwnersAsync(NotificationEventType.BackupFailed, payload, ct);
    }

    public Task<Dictionary<string, bool>> SendChatHealthWarningAsync(
        string chatName,
        string status,
        bool isAdmin,
        IReadOnlyList<string> warnings,
        CancellationToken ct = default)
    {
        var builder = NotificationPayloadBuilder.Create("Chat Health Warning")
            .WithField("Chat", chatName)
            .WithField("Status", status)
            .WithField("Bot is admin", isAdmin ? "Yes" : "No");

        if (warnings.Count > 0)
        {
            builder.WithSection("Warnings", s =>
            {
                foreach (var warning in warnings)
                    s.WithText(warning);
            });
        }

        return SendToOwnersAsync(NotificationEventType.ChatHealthWarning, builder.Build(), ct);
    }

    // ════════════════════════════════════════════════════════════════════════════
    // Audience Routing
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Route notification to chat audience using two-pool model:
    /// Pool 1: Web users with chat access (3-channel delivery per preferences)
    /// Pool 2: Unlinked Telegram chat admins with BotDmEnabled (DM only, when chat is provided)
    /// Deduplicates by Telegram ID to prevent double-DM.
    /// When chat is null, repo returns >= GlobalAdmin only (no pool 2).
    /// </summary>
    private async Task<Dictionary<string, bool>> SendToChatAudienceAsync(
        ChatIdentity? chat,
        NotificationEventType eventType,
        NotificationPayload payload,
        CancellationToken ct)
    {
        try
        {
            var results = new Dictionary<string, bool>();

            // Pool 1: Web users with chat access (deduplicated by web user ID in the repo query)
            var webUsers = await _userRepo.GetWebUsersWithChatAccessAsync(chat?.Id, ct);

            if (webUsers.Count == 0)
            {
                _logger.LogWarning(
                    "No eligible web user recipients for {EventType} notification (chat: {Chat})",
                    eventType, chat?.ToLogDebug() ?? "global");
            }

            _logger.LogInformation(
                "Sending {EventType} notification to {WebUserCount} web user(s) + pool 2 (chat: {Chat})",
                eventType, webUsers.Count, chat?.ToLogInfo() ?? "global");

            // Collect linked Telegram IDs from pool 1 for dedup
            var pool1TelegramIds = new HashSet<long>();

            foreach (var user in webUsers)
            {
                var success = await SendToUserAsync(user, eventType, payload, ct);
                results[user.WebUser.Id] = success;

                // Track this web user's linked Telegram IDs for dedup
                var mappings = await _telegramMappingRepo.GetByUserIdAsync(user.WebUser.Id, ct);
                foreach (var mapping in mappings)
                    pool1TelegramIds.Add(mapping.TelegramId);
            }

            // Pool 2: Unlinked Telegram chat admins (DM only) — only when chat is provided
            if (chat != null)
            {
                var chatAdmins = await _chatAdminsRepo.GetChatAdminsAsync(chat.Id, ct);
                foreach (var admin in chatAdmins)
                {
                    // Skip admins with linked web accounts (already in pool 1)
                    if (admin.LinkedWebUser != null) continue;

                    // Skip if BotDmEnabled is false (hasn't run /start)
                    if (!admin.BotDmEnabled) continue;

                    // Dedup by Telegram ID (shouldn't happen here since we skipped linked, but safety net)
                    if (pool1TelegramIds.Contains(admin.User.Id)) continue;

                    await SendTelegramDmDirectAsync(admin.User.Id, payload, ct);
                }
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send {EventType} notification (chat: {Chat})",
                eventType, chat?.ToLogDebug() ?? "global");
            return new Dictionary<string, bool>();
        }
    }

    /// <summary>
    /// Route notification to Owner users only (infrastructure events).
    /// </summary>
    private async Task<Dictionary<string, bool>> SendToOwnersAsync(
        NotificationEventType eventType,
        NotificationPayload payload,
        CancellationToken ct)
    {
        try
        {
            var allUsers = await _userRepo.GetAllAsync(ct);
            var owners = allUsers.Where(u => u.WebUser.PermissionLevel == PermissionLevel.Owner).ToList();

            if (owners.Count == 0)
            {
                _logger.LogWarning("No Owner users found for {EventType} notification", eventType);
                return new Dictionary<string, bool>();
            }

            _logger.LogInformation(
                "Sending {EventType} notification to {OwnerCount} Owner user(s)",
                eventType, owners.Count);

            var results = new Dictionary<string, bool>();
            foreach (var owner in owners)
            {
                var success = await SendToUserAsync(owner, eventType, payload, ct);
                results[owner.WebUser.Id] = success;
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send {EventType} notification to owners", eventType);
            return new Dictionary<string, bool>();
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    // Per-User Delivery (Typed Methods)
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Send notification to a web user across all enabled channels.
    /// Uses NotificationRenderer for channel-specific formatting.
    /// </summary>
    private async Task<bool> SendToUserAsync(
        UserRecord user,
        NotificationEventType eventType,
        NotificationPayload payload,
        CancellationToken ct)
    {
        try
        {
            var config = await _preferencesRepo.GetOrCreateAsync(user.WebUser.Id, ct);
            var deliverySuccess = false;

            // Telegram DM channel
            if (config.IsEnabled(NotificationChannel.TelegramDm, eventType))
            {
                var success = await SendTypedTelegramDmAsync(user, payload, ct);
                deliverySuccess = deliverySuccess || success;
            }

            // Email channel
            if (config.IsEnabled(NotificationChannel.Email, eventType))
            {
                var success = await SendTypedEmailAsync(user, payload, ct);
                deliverySuccess = deliverySuccess || success;
            }

            // WebPush channel
            if (config.IsEnabled(NotificationChannel.WebPush, eventType))
            {
                var plainText = NotificationRenderer.ToPlainText(payload);
                var success = await _webPushService.SendAsync(user, eventType, payload.Subject, plainText, ct);
                deliverySuccess = deliverySuccess || success;
            }

            if (!deliverySuccess)
            {
                LogDeliveryFailure(user, eventType, config);
            }

            return deliverySuccess;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send typed notification to {User} for {EventType}",
                user.ToLogDebug(), eventType);
            return false;
        }
    }

    /// <summary>
    /// Send Telegram DM to a web user using typed payload (HTML format).
    /// Resolves Telegram ID from web user's linked account.
    /// </summary>
    private async Task<bool> SendTypedTelegramDmAsync(
        UserRecord user,
        NotificationPayload payload,
        CancellationToken ct)
    {
        try
        {
            var mappings = await _telegramMappingRepo.GetByUserIdAsync(user.WebUser.Id, ct);
            var mapping = mappings.FirstOrDefault();

            if (mapping == null)
            {
                _logger.LogDebug("User {User} has no Telegram account linked, skipping DM",
                    user.ToLogDebug());
                return false;
            }

            var htmlMessage = NotificationRenderer.ToTelegramHtml(payload);

            // Build keyboard if payload has action context
            InlineKeyboardMarkup? keyboard = null;
            if (payload.Keyboard is { } kb)
            {
                keyboard = await BuildReportActionKeyboardAsync(
                    kb.EntityId, kb.ChatId, kb.UserId, kb.KeyboardType, ct);
            }

            DmDeliveryResult result;

            // Use rich method if we have media or keyboard
            if (keyboard != null || !string.IsNullOrWhiteSpace(payload.PhotoPath) || !string.IsNullOrWhiteSpace(payload.VideoPath))
            {
                result = await _dmDeliveryService.SendDmWithMediaAndKeyboardAsync(
                    mapping.TelegramId,
                    "notification",
                    htmlMessage,
                    photoPath: payload.PhotoPath,
                    videoPath: payload.VideoPath,
                    keyboard: keyboard,
                    parseMode: ParseMode.Html,
                    cancellationToken: ct);
            }
            else
            {
                result = await _dmDeliveryService.SendDmWithQueueAsync(
                    mapping.TelegramId,
                    "notification",
                    htmlMessage,
                    parseMode: ParseMode.Html,
                    cancellationToken: ct);
            }

            if (result.DmSent)
            {
                _logger.LogInformation("Sent typed Telegram DM to {User}", user.ToLogInfo());
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send typed Telegram DM to {User}", user.ToLogDebug());
            return false;
        }
    }

    /// <summary>
    /// Send Telegram DM directly to an unlinked admin (pool 2).
    /// No preferences check — just render and send. Gracefully handles blocked bot.
    /// </summary>
    private async Task SendTelegramDmDirectAsync(
        long telegramId,
        NotificationPayload payload,
        CancellationToken ct)
    {
        try
        {
            var htmlMessage = NotificationRenderer.ToTelegramHtml(payload);

            // Build keyboard if payload has action context
            InlineKeyboardMarkup? keyboard = null;
            if (payload.Keyboard is { } kb)
            {
                keyboard = await BuildReportActionKeyboardAsync(
                    kb.EntityId, kb.ChatId, kb.UserId, kb.KeyboardType, ct);
            }

            if (keyboard != null || !string.IsNullOrWhiteSpace(payload.PhotoPath) || !string.IsNullOrWhiteSpace(payload.VideoPath))
            {
                await _dmDeliveryService.SendDmWithMediaAndKeyboardAsync(
                    telegramId,
                    "notification",
                    htmlMessage,
                    photoPath: payload.PhotoPath,
                    videoPath: payload.VideoPath,
                    keyboard: keyboard,
                    parseMode: ParseMode.Html,
                    cancellationToken: ct);
            }
            else
            {
                await _dmDeliveryService.SendDmWithQueueAsync(
                    telegramId,
                    "notification",
                    htmlMessage,
                    parseMode: ParseMode.Html,
                    cancellationToken: ct);
            }
        }
        catch (Exception ex)
        {
            // Graceful failure — BotDmService already handles 403 by setting BotDmEnabled=false.
            // Any other error is unexpected but not worth failing the whole notification for.
            _logger.LogDebug(ex, "Failed to send DM to unlinked admin {TelegramId}", telegramId);
        }
    }

    /// <summary>
    /// Send email using rendered HTML payload.
    /// </summary>
    private async Task<bool> SendTypedEmailAsync(
        UserRecord user,
        NotificationPayload payload,
        CancellationToken ct)
    {
        try
        {
            var emailAddress = user.WebUser.Email!;
            var htmlBody = NotificationRenderer.ToEmailHtml(payload);

            await _emailService.SendEmailAsync(emailAddress, payload.Subject, htmlBody, isHtml: true, ct);

            _logger.LogInformation("Sent email notification to {User}", user.ToLogInfo());
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {User}", user.ToLogDebug());
            return false;
        }
    }

    /// <summary>
    /// Log delivery failure with throttling to avoid spam during network outages.
    /// </summary>
    private void LogDeliveryFailure(UserRecord user, NotificationEventType eventType, NotificationConfig config)
    {
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

    // ════════════════════════════════════════════════════════════════════════════
    // Shared Infrastructure (used by both typed and legacy methods)
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Build inline keyboard with report moderation action buttons.
    /// Creates ONE database-backed callback context per report (short IDs, no 64-byte limit issues).
    /// </summary>
    private async Task<InlineKeyboardMarkup> BuildReportActionKeyboardAsync(
        long reportId,
        long chatId,
        long userId,
        ReportType reportType,
        CancellationToken cancellationToken)
    {
        var context = new ReportCallbackContext(
            Id: 0,
            ReportId: reportId,
            ReportType: reportType,
            ChatId: chatId,
            UserId: userId,
            CreatedAt: DateTimeOffset.UtcNow);
        var contextId = await _callbackContextRepo.CreateAsync(context, cancellationToken);

        return reportType switch
        {
            ReportType.ExamFailure => new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("✓ Approve", $"rev:{contextId}:{(int)ExamAction.Approve}"),
                    InlineKeyboardButton.WithCallbackData("✗ Deny", $"rev:{contextId}:{(int)ExamAction.Deny}")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🚫 Deny & Ban", $"rev:{contextId}:{(int)ExamAction.DenyAndBan}")
                }
            }),
            ReportType.ContentReport => new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🚫 Spam", $"rev:{contextId}:{(int)ReportAction.Spam}"),
                    InlineKeyboardButton.WithCallbackData("⛔ Ban", $"rev:{contextId}:{(int)ReportAction.Ban}")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("⚠️ Warn", $"rev:{contextId}:{(int)ReportAction.Warn}"),
                    InlineKeyboardButton.WithCallbackData("✓ Dismiss", $"rev:{contextId}:{(int)ReportAction.Dismiss}")
                }
            }),
            ReportType.ImpersonationAlert => new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🚫 Confirm", $"rev:{contextId}:{(int)ImpersonationAction.Confirm}"),
                    InlineKeyboardButton.WithCallbackData("✓ Dismiss", $"rev:{contextId}:{(int)ImpersonationAction.Dismiss}")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🤝 Trust User", $"rev:{contextId}:{(int)ImpersonationAction.Trust}")
                }
            }),
            ReportType.ProfileScanAlert => new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("✓ Allow", $"rev:{contextId}:{(int)ProfileScanAction.Allow}"),
                    InlineKeyboardButton.WithCallbackData("⛔ Ban", $"rev:{contextId}:{(int)ProfileScanAction.Ban}")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("👢 Kick", $"rev:{contextId}:{(int)ProfileScanAction.Kick}")
                }
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(reportType), reportType, "Unknown report type")
        };
    }

    // ════════════════════════════════════════════════════════════════════════════
    // Legacy Methods (kept for caller migration in next commit)
    // ════════════════════════════════════════════════════════════════════════════

    public async Task<Dictionary<string, bool>> SendChatNotificationAsync(
        ChatIdentity? chat,
        NotificationEventType eventType,
        string subject,
        string message,
        long? reportId = null,
        string? photoPath = null,
        long? reportedUserId = null,
        ReportType? reportType = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var recipients = new Dictionary<string, UserRecord>();

            var allUsers = await _userRepo.GetAllAsync(cancellationToken);
            foreach (var user in allUsers.Where(u => u.WebUser.PermissionLevel >= PermissionLevel.GlobalAdmin))
                recipients.TryAdd(user.WebUser.Id, user);

            if (chat != null)
            {
                var chatAdmins = await _chatAdminsRepo.GetChatAdminsAsync(chat.Id, cancellationToken);
                foreach (var admin in chatAdmins.Where(a => a.LinkedWebUser != null))
                    recipients.TryAdd(admin.LinkedWebUser!.WebUser.Id, admin.LinkedWebUser);
            }

            if (recipients.Count == 0)
            {
                _logger.LogWarning(
                    "No eligible recipients for {EventType} notification (chat: {Chat})",
                    eventType, chat?.ToLogDebug() ?? "global");
                return new Dictionary<string, bool>();
            }

            _logger.LogInformation(
                "Sending {EventType} notification to {UserCount} recipient(s) (chat: {Chat})",
                eventType, recipients.Count, chat?.ToLogInfo() ?? "global");

            var results = new Dictionary<string, bool>();
            foreach (var user in recipients.Values)
            {
                var success = await SendNotificationAsync(
                    user, eventType, subject, message,
                    reportId, photoPath, reportedUserId, chat?.Id, reportType,
                    cancellationToken);
                results[user.WebUser.Id] = success;
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send {EventType} notification (chat: {Chat})",
                eventType, chat?.ToLogDebug() ?? "global");
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
            var allUsers = await _userRepo.GetAllAsync(cancellationToken);
            var ownerUsers = allUsers.Where(u => u.WebUser.PermissionLevel == PermissionLevel.Owner).ToList();

            if (ownerUsers.Count == 0)
            {
                _logger.LogWarning("No Owner users found in system, cannot send system notification");
                return new Dictionary<string, bool>();
            }

            _logger.LogInformation(
                "Sending system notification to {OwnerCount} Owner user(s) for event {EventType}",
                ownerUsers.Count, eventType);

            var results = new Dictionary<string, bool>();
            foreach (var owner in ownerUsers)
            {
                var success = await SendNotificationAsync(owner, eventType, subject, message, cancellationToken);
                results[owner.WebUser.Id] = success;
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
        return await SendNotificationAsync(
            user, eventType, subject, message,
            reportId: null, photoPath: null, reportedUserId: null, chatId: null, reportType: null,
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
        ReportType? reportType,
        CancellationToken cancellationToken)
    {
        try
        {
            var config = await _preferencesRepo.GetOrCreateAsync(user.WebUser.Id, cancellationToken);

            var deliverySuccess = false;

            if (config.IsEnabled(NotificationChannel.TelegramDm, eventType))
            {
                var telegramSuccess = await SendLegacyTelegramDmAsync(
                    user.WebUser.Id, subject, message,
                    reportId, photoPath, reportedUserId, chatId, reportType,
                    cancellationToken);
                deliverySuccess = deliverySuccess || telegramSuccess;
            }

            if (config.IsEnabled(NotificationChannel.Email, eventType))
            {
                var emailSuccess = await SendLegacyEmailAsync(user, subject, message, cancellationToken);
                deliverySuccess = deliverySuccess || emailSuccess;
            }

            if (config.IsEnabled(NotificationChannel.WebPush, eventType))
            {
                var webPushSuccess = await _webPushService.SendAsync(user, eventType, subject, message, cancellationToken);
                deliverySuccess = deliverySuccess || webPushSuccess;
            }

            if (!deliverySuccess)
            {
                LogDeliveryFailure(user, eventType, config);
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

    private async Task<bool> SendLegacyTelegramDmAsync(
        string userId,
        string subject,
        string message,
        long? reportId,
        string? photoPath,
        long? reportedUserId,
        long? chatId,
        ReportType? reportType,
        CancellationToken cancellationToken)
    {
        var user = await _userRepo.GetByIdAsync(userId, cancellationToken);

        try
        {
            var mappings = await _telegramMappingRepo.GetByUserIdAsync(userId, cancellationToken);
            var mapping = mappings.FirstOrDefault();

            if (mapping == null)
            {
                _logger.LogDebug("User {User} has no Telegram account linked, skipping Telegram DM",
                    user.ToLogDebug(userId));
                return false;
            }

            var formattedMessage = $"🔔 *{TelegramTextUtilities.EscapeMarkdownV2(subject)}*\n\n{TelegramTextUtilities.EscapeMarkdownV2(message)}";

            InlineKeyboardMarkup? keyboard = null;
            if (reportId.HasValue && reportedUserId.HasValue && chatId.HasValue && reportType.HasValue)
            {
                keyboard = await BuildReportActionKeyboardAsync(
                    reportId.Value, chatId.Value, reportedUserId.Value,
                    reportType.Value,
                    cancellationToken);
            }

            DmDeliveryResult result;

            if (keyboard != null || !string.IsNullOrWhiteSpace(photoPath))
            {
                result = await _dmDeliveryService.SendDmWithMediaAndKeyboardAsync(
                    mapping.TelegramId,
                    "report",
                    formattedMessage,
                    photoPath: photoPath,
                    keyboard: keyboard,
                    cancellationToken: cancellationToken);
            }
            else
            {
                result = await _dmDeliveryService.SendDmWithQueueAsync(
                    mapping.TelegramId,
                    "notification",
                    formattedMessage,
                    cancellationToken: cancellationToken);
            }

            if (result.DmSent)
            {
                _logger.LogInformation("Sent Telegram DM notification to {User} (Telegram ID {TelegramId})",
                    user.ToLogInfo(userId), mapping.TelegramId);
                return true;
            }

            if (!result.Failed)
            {
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

    private async Task<bool> SendLegacyEmailAsync(
        UserRecord user,
        string subject,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            var emailAddress = user.WebUser.Email!;

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
}
