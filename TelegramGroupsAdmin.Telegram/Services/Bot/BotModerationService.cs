using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Handlers;

namespace TelegramGroupsAdmin.Telegram.Services.Bot;

/// <summary>
/// Manager/Worker orchestration layer for moderation actions.
/// The "boss" that knows all workers, decides who to call, and owns business rules.
///
/// Key responsibilities:
/// - System account protection (blocks moderation on Telegram system accounts: 777000, 1087968824, etc.)
/// - Business rules: "bans revoke trust", "N warnings = auto-ban"
/// - Workflow composition: warn → check threshold → ban
/// - Direct handler calls (no event broadcasting)
///
/// Workers are domain experts that don't know about each other.
/// Only the orchestrator composes workflows across workers.
/// </summary>
public class BotModerationService : IBotModerationService
{
    // Domain handlers (workers)
    private readonly IBotBanHandler _banHandler;
    private readonly ITrustHandler _trustHandler;
    private readonly IWarnHandler _warnHandler;
    private readonly IBotModerationMessageHandler _messageHandler;
    private readonly IBotRestrictHandler _restrictHandler;

    // Support handlers
    private readonly IAuditHandler _auditHandler;
    private readonly INotificationHandler _notificationHandler;
    private readonly ITrainingHandler _trainingHandler;

    // Repositories for logging
    private readonly ITelegramUserRepository _userRepository;
    private readonly IManagedChatsRepository _managedChatsRepository;

    // Services
    private readonly IBanCelebrationService _banCelebrationService;
    private readonly IReportService _reportService;
    private readonly INotificationService _notificationService;

    // Configuration
    private readonly IConfigService _configService;
    private readonly ILogger<BotModerationService> _logger;

    public BotModerationService(
        IBotBanHandler banHandler,
        ITrustHandler trustHandler,
        IWarnHandler warnHandler,
        IBotModerationMessageHandler messageHandler,
        IBotRestrictHandler restrictHandler,
        IAuditHandler auditHandler,
        INotificationHandler notificationHandler,
        ITrainingHandler trainingHandler,
        ITelegramUserRepository userRepository,
        IManagedChatsRepository managedChatsRepository,
        IBanCelebrationService banCelebrationService,
        IReportService reportService,
        INotificationService notificationService,
        IConfigService configService,
        ILogger<BotModerationService> logger)
    {
        _banHandler = banHandler;
        _trustHandler = trustHandler;
        _warnHandler = warnHandler;
        _messageHandler = messageHandler;
        _restrictHandler = restrictHandler;
        _auditHandler = auditHandler;
        _notificationHandler = notificationHandler;
        _trainingHandler = trainingHandler;
        _userRepository = userRepository;
        _managedChatsRepository = managedChatsRepository;
        _banCelebrationService = banCelebrationService;
        _reportService = reportService;
        _notificationService = notificationService;
        _configService = configService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> MarkAsSpamAndBanAsync(
        long messageId,
        long userId,
        long chatId,
        Actor executor,
        string reason,
        global::Telegram.Bot.Types.Message? telegramMessage = null,
        CancellationToken cancellationToken = default)
    {
        var protectionResult = await CheckServiceAccountProtectionAsync(userId, cancellationToken);
        if (protectionResult != null) return protectionResult;

        // Step 1: Ensure message exists in database (backfill if needed for training data)
        await _messageHandler.EnsureExistsAsync(messageId, chatId, telegramMessage, cancellationToken);

        // Step 2: Delete message (best effort - may already be deleted)
        var deleteResult = await _messageHandler.DeleteAsync(chatId, messageId, executor, cancellationToken);
        if (deleteResult.MessageDeleted)
        {
            await SafeAuditAsync(
                () => _auditHandler.LogDeleteAsync(messageId, chatId, userId, executor, cancellationToken),
                "message deletion", userId, chatId);
        }

        // Step 3: Ban user globally (inline - don't call BanUserAsync to control notification)
        var banResult = await _banHandler.BanAsync(userId, executor, reason, messageId, cancellationToken);

        if (!banResult.Success)
        {
            return new ModerationResult
            {
                Success = false,
                ErrorMessage = banResult.ErrorMessage,
                MessageDeleted = deleteResult.MessageDeleted
            };
        }

        // Audit successful ban
        await SafeAuditAsync(
            () => _auditHandler.LogBanAsync(userId, executor, reason, cancellationToken),
            "ban", userId);

        // Business rule: Bans always revoke trust
        var untrustReason = string.IsNullOrWhiteSpace(reason)
            ? "Trust revoked due to ban"
            : $"Trust revoked due to ban: {reason}";
        var untrustResult = await _trustHandler.UntrustAsync(userId, executor, untrustReason, cancellationToken);

        if (untrustResult.Success)
        {
            await SafeAuditAsync(
                () => _auditHandler.LogUntrustAsync(userId, executor, untrustReason, cancellationToken),
                "untrust (from ban)", userId);
        }

        // Schedule cleanup of user's messages
        await SafeExecuteAsync(
            () => _messageHandler.ScheduleUserMessagesCleanupAsync(userId, cancellationToken),
            $"Schedule messages cleanup for user {userId}");

        // Step 4: Create training data (non-critical - failure doesn't affect ban success)
        await SafeExecuteAsync(
            () => _trainingHandler.CreateSpamSampleAsync(messageId, executor, cancellationToken),
            $"Create training data for message {messageId}");

        // Step 5: Send ban celebration (non-critical - failure doesn't affect ban success)
        await SafeExecuteAsync(
            async () =>
            {
                // Get chat name for celebration
                var chat = await _managedChatsRepository.GetByChatIdAsync(chatId, cancellationToken);
                var chatName = chat?.ChatName ?? chatId.ToString();

                // Get user display name
                var user = await _userRepository.GetByTelegramIdAsync(userId, cancellationToken);
                var userName = user != null
                    ? TelegramDisplayName.Format(user.FirstName, user.LastName, user.Username, userId)
                    : userId.ToString();

                await _banCelebrationService.SendBanCelebrationAsync(
                    chatId, chatName, userId, userName, isAutoBan: false, cancellationToken);
            },
            $"Send ban celebration for user {userId} in chat {chatId}");

        // Step 6: Rich admin notification (replaces simple notification from BanUserAsync)
        await SafeExecuteAsync(
            async () =>
            {
                var enrichedMessage = await _messageHandler.GetEnrichedAsync(messageId, cancellationToken);
                if (enrichedMessage != null)
                {
                    await _notificationHandler.NotifyAdminsSpamBanAsync(
                        enrichedMessage,
                        banResult.ChatsAffected,
                        deleteResult.MessageDeleted,
                        cancellationToken);
                }
                else
                {
                    // Fallback if message not found (shouldn't happen, but defensive)
                    await _notificationHandler.NotifyAdminsBanAsync(userId, executor, reason, cancellationToken);
                }
            },
            $"Rich spam notification for user {userId}");

        return new ModerationResult
        {
            Success = true,
            ChatsAffected = banResult.ChatsAffected,
            MessageDeleted = deleteResult.MessageDeleted,
            TrustRemoved = untrustResult.Success
        };
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> BanUserAsync(
        long userId,
        long? messageId,
        Actor executor,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var protectionResult = await CheckServiceAccountProtectionAsync(userId, cancellationToken);
        if (protectionResult != null) return protectionResult;

        // Primary action: Ban globally
        var banResult = await _banHandler.BanAsync(userId, executor, reason, messageId, cancellationToken);

        if (!banResult.Success)
            return ModerationResult.Failed(banResult.ErrorMessage ?? "Ban failed");

        // Audit successful ban (separate from BanHandler's state tracking record)
        await SafeAuditAsync(
            () => _auditHandler.LogBanAsync(userId, executor, reason, cancellationToken),
            "ban", userId);

        // Business rule: Bans always revoke trust
        var untrustReason = string.IsNullOrWhiteSpace(reason)
            ? "Trust revoked due to ban"
            : $"Trust revoked due to ban: {reason}";
        var untrustResult = await _trustHandler.UntrustAsync(
            userId, executor, untrustReason, cancellationToken);

        if (untrustResult.Success)
        {
            await SafeAuditAsync(
                () => _auditHandler.LogUntrustAsync(userId, executor, untrustReason, cancellationToken),
                "untrust (from ban)", userId);
        }

        // Notify admins
        await _notificationHandler.NotifyAdminsBanAsync(userId, executor, reason, cancellationToken);

        // Schedule cleanup of user's messages (non-critical - don't fail the ban if this fails)
        await SafeExecuteAsync(
            () => _messageHandler.ScheduleUserMessagesCleanupAsync(userId, cancellationToken),
            $"Schedule messages cleanup for user {userId}");

        return new ModerationResult
        {
            Success = true,
            ChatsAffected = banResult.ChatsAffected,
            TrustRemoved = untrustResult.Success
        };
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> WarnUserAsync(
        long userId,
        long? messageId,
        Actor executor,
        string reason,
        long chatId,
        CancellationToken cancellationToken = default)
    {
        var protectionResult = await CheckServiceAccountProtectionAsync(userId, cancellationToken);
        if (protectionResult != null) return protectionResult;

        // Fetch once for logging
        var user = await _userRepository.GetByTelegramIdAsync(userId, cancellationToken);

        // Primary action: Issue warning (writes to warnings table)
        var warnResult = await _warnHandler.WarnAsync(userId, executor, reason, chatId, messageId, cancellationToken);

        if (!warnResult.Success)
            return new ModerationResult { Success = false, ErrorMessage = warnResult.ErrorMessage };

        // Audit successful warning
        await SafeAuditAsync(
            () => _auditHandler.LogWarnAsync(userId, executor, reason, cancellationToken),
            "warning", userId, chatId);

        // Notify user about warning
        await _notificationHandler.NotifyUserWarningAsync(userId, warnResult.WarningCount, reason, cancellationToken);

        var result = new ModerationResult
        {
            Success = true,
            WarningCount = warnResult.WarningCount
        };

        // Business rule: Check warning threshold for auto-ban
        var warningConfig = await _configService.GetEffectiveAsync<WarningSystemConfig>(
            ConfigType.Moderation, chatId) ?? WarningSystemConfig.Default;

        if (warningConfig.AutoBanEnabled &&
            warningConfig.AutoBanThreshold > 0 &&
            warnResult.WarningCount >= warningConfig.AutoBanThreshold)
        {
            _logger.LogWarning(
                "Auto-ban triggered: {User} has {WarnCount} warnings (threshold: {Threshold})",
                user.ToLogDebug(userId), warnResult.WarningCount, warningConfig.AutoBanThreshold);

            // Use configured auto-ban reason with {count} placeholder support
            var autoBanReason = !string.IsNullOrWhiteSpace(warningConfig.AutoBanReason)
                ? warningConfig.AutoBanReason.Replace("{count}", warnResult.WarningCount.ToString())
                : $"Exceeded warning threshold ({warnResult.WarningCount}/{warningConfig.AutoBanThreshold} warnings)";

            // Auto-ban: Call handlers directly (don't call BanUserAsync to avoid nested orchestrator calls)
            var banResult = await _banHandler.BanAsync(userId, Actor.AutoBan, autoBanReason, messageId, cancellationToken);

            if (banResult.Success)
            {
                // Audit successful auto-ban
                await SafeAuditAsync(
                    () => _auditHandler.LogBanAsync(userId, Actor.AutoBan, autoBanReason, cancellationToken),
                    "auto-ban (from warnings)", userId, chatId);

                // Business rule: Bans always revoke trust
                var untrustReason = $"Trust revoked: {autoBanReason}";
                var untrustResult = await _trustHandler.UntrustAsync(userId, Actor.AutoBan, untrustReason, cancellationToken);
                if (untrustResult.Success)
                {
                    await SafeAuditAsync(
                        () => _auditHandler.LogUntrustAsync(userId, Actor.AutoBan, untrustReason, cancellationToken),
                        "untrust (from auto-ban)", userId, chatId);
                }

                // Notify admins (simple notification - no detection context for warning-based bans)
                await _notificationHandler.NotifyAdminsBanAsync(userId, Actor.AutoBan, autoBanReason, cancellationToken);

                // Schedule cleanup of user's messages
                await SafeExecuteAsync(
                    () => _messageHandler.ScheduleUserMessagesCleanupAsync(userId, cancellationToken),
                    $"Schedule messages cleanup for user {userId}");

                result = result with
                {
                    AutoBanTriggered = true,
                    ChatsAffected = banResult.ChatsAffected,
                    TrustRemoved = untrustResult.Success
                };
            }
            else
            {
                _logger.LogError(
                    "Auto-ban failed for {User}: {Error}",
                    user.ToLogDebug(userId), banResult.ErrorMessage);
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> TrustUserAsync(
        long userId,
        Actor executor,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var trustResult = await _trustHandler.TrustAsync(userId, executor, reason, cancellationToken);

        if (!trustResult.Success)
            return new ModerationResult { Success = false, ErrorMessage = trustResult.ErrorMessage };

        // Audit successful trust
        await SafeAuditAsync(
            () => _auditHandler.LogTrustAsync(userId, executor, reason, cancellationToken),
            "trust", userId);

        return new ModerationResult { Success = true };
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> UntrustUserAsync(
        long userId,
        Actor executor,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var untrustResult = await _trustHandler.UntrustAsync(userId, executor, reason, cancellationToken);

        if (!untrustResult.Success)
            return new ModerationResult { Success = false, ErrorMessage = untrustResult.ErrorMessage };

        // Audit successful untrust
        await SafeAuditAsync(
            () => _auditHandler.LogUntrustAsync(userId, executor, reason, cancellationToken),
            "untrust", userId);

        return new ModerationResult { Success = true };
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> UnbanUserAsync(
        long userId,
        Actor executor,
        string reason,
        bool restoreTrust = false,
        CancellationToken cancellationToken = default)
    {
        var unbanResult = await _banHandler.UnbanAsync(userId, executor, reason, cancellationToken);

        if (!unbanResult.Success)
            return new ModerationResult { Success = false, ErrorMessage = unbanResult.ErrorMessage };

        // Audit successful unban
        await SafeAuditAsync(
            () => _auditHandler.LogUnbanAsync(userId, executor, reason, cancellationToken),
            "unban", userId);

        var result = new ModerationResult
        {
            Success = true,
            ChatsAffected = unbanResult.ChatsAffected
        };

        // Handle trust restoration as follow-up
        if (restoreTrust)
        {
            var trustResult = await TrustUserAsync(
                userId, executor,
                "Trust restored after unban (false positive correction)", cancellationToken);
            result = result with { TrustRestored = trustResult.Success };
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> DeleteMessageAsync(
        long messageId,
        long chatId,
        long userId,
        Actor deletedBy,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var deleteResult = await _messageHandler.DeleteAsync(chatId, messageId, deletedBy, cancellationToken);

        // Audit the deletion attempt (even if message was already deleted)
        await SafeAuditAsync(
            () => _auditHandler.LogDeleteAsync(messageId, chatId, userId, deletedBy, cancellationToken),
            "message deletion", userId, chatId);

        return new ModerationResult
        {
            Success = true,
            MessageDeleted = deleteResult.MessageDeleted
        };
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> TempBanUserAsync(
        long userId,
        long? messageId,
        Actor executor,
        string reason,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var protectionResult = await CheckServiceAccountProtectionAsync(userId, cancellationToken);
        if (protectionResult != null) return protectionResult;

        var tempBanResult = await _banHandler.TempBanAsync(userId, executor, duration, reason, messageId, cancellationToken);

        if (!tempBanResult.Success)
            return new ModerationResult { Success = false, ErrorMessage = tempBanResult.ErrorMessage };

        // Audit successful temp ban (separate from BanHandler's state tracking record)
        await SafeAuditAsync(
            () => _auditHandler.LogTempBanAsync(userId, executor, duration, reason, cancellationToken),
            "temp ban", userId);

        // Notify user about temp ban with rejoin info
        await _notificationHandler.NotifyUserTempBanAsync(userId, duration, tempBanResult.ExpiresAt, reason, cancellationToken);

        return new ModerationResult
        {
            Success = true,
            ChatsAffected = tempBanResult.ChatsAffected
        };
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> RestrictUserAsync(
        long userId,
        long? messageId,
        Actor executor,
        string reason,
        TimeSpan duration,
        long? chatId = null,
        CancellationToken cancellationToken = default)
    {
        var protectionResult = await CheckServiceAccountProtectionAsync(userId, cancellationToken);
        if (protectionResult != null) return protectionResult;

        var restrictResult = await _restrictHandler.RestrictAsync(
            userId, chatId ?? 0, executor, duration, reason, cancellationToken);

        if (!restrictResult.Success)
            return new ModerationResult { Success = false, ErrorMessage = restrictResult.ErrorMessage };

        // Audit successful restriction
        await SafeAuditAsync(
            () => _auditHandler.LogRestrictAsync(userId, chatId ?? 0, executor, reason, cancellationToken),
            "restriction", userId, chatId);

        return new ModerationResult
        {
            Success = true,
            ChatsAffected = restrictResult.ChatsAffected
        };
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> SyncBanToChatAsync(
        User user,
        Chat chat,
        string reason,
        long? triggeredByMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var protectionResult = await CheckServiceAccountProtectionAsync(user.Id, cancellationToken);
        if (protectionResult != null) return protectionResult;

        var banResult = await _banHandler.BanAsync(
            user, chat, Actor.AutoDetection, reason, triggeredByMessageId, cancellationToken);

        if (!banResult.Success)
            return ModerationResult.Failed(banResult.ErrorMessage ?? "Ban sync failed");

        // Audit successful ban sync
        await SafeAuditAsync(
            () => _auditHandler.LogBanAsync(user.Id, Actor.AutoDetection, reason, cancellationToken),
            "ban sync", user.Id, chat.Id);

        return new ModerationResult
        {
            Success = true,
            ChatsAffected = banResult.ChatsAffected
        };
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> RestoreUserPermissionsAsync(
        long userId,
        long chatId,
        Actor executor,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var protectionResult = await CheckServiceAccountProtectionAsync(userId, cancellationToken);
        if (protectionResult != null) return protectionResult;

        var restrictResult = await _restrictHandler.RestorePermissionsAsync(
            userId, chatId, executor, reason, cancellationToken);

        if (!restrictResult.Success)
            return ModerationResult.Failed(restrictResult.ErrorMessage ?? "Failed to restore permissions");

        // Audit successful permission restoration
        await SafeAuditAsync(
            () => _auditHandler.LogRestorePermissionsAsync(userId, chatId, executor, reason, cancellationToken),
            "restore permissions", userId, chatId);

        return new ModerationResult
        {
            Success = true,
            ChatsAffected = restrictResult.ChatsAffected
        };
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> KickUserFromChatAsync(
        long userId,
        long chatId,
        Actor executor,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var protectionResult = await CheckServiceAccountProtectionAsync(userId, cancellationToken);
        if (protectionResult != null) return protectionResult;

        var kickResult = await _banHandler.KickFromChatAsync(
            userId, chatId, executor, reason, cancellationToken);

        if (!kickResult.Success)
            return ModerationResult.Failed(kickResult.ErrorMessage ?? "Failed to kick user");

        // Audit successful kick
        await SafeAuditAsync(
            () => _auditHandler.LogKickAsync(userId, chatId, executor, reason, cancellationToken),
            "kick", userId, chatId);

        return new ModerationResult
        {
            Success = true,
            ChatsAffected = kickResult.ChatsAffected
        };
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> HandleMalwareViolationAsync(
        long messageId,
        long chatId,
        long userId,
        string malwareDetails,
        Message? telegramMessage = null,
        CancellationToken cancellationToken = default)
    {
        // Fetch once for logging
        var user = await _userRepository.GetByTelegramIdAsync(userId, cancellationToken);
        var chat = await _managedChatsRepository.GetByChatIdAsync(chatId, cancellationToken);

        _logger.LogWarning(
            "Handling malware violation for message {MessageId} from {User} in {Chat}: {Details}",
            messageId, user.ToLogDebug(userId), chat.ToLogDebug(chatId), malwareDetails);

        // Step 1: Ensure message exists in database (for audit trail)
        await _messageHandler.EnsureExistsAsync(messageId, chatId, telegramMessage, cancellationToken);

        // Step 2: Delete the malware message
        var deleteResult = await _messageHandler.DeleteAsync(chatId, messageId, Actor.FileScanner, cancellationToken);

        await SafeAuditAsync(
            () => _auditHandler.LogDeleteAsync(messageId, chatId, userId, Actor.FileScanner, cancellationToken),
            "malware deletion", userId, chatId);

        // Step 3: Create admin report for review
        await SafeExecuteAsync(async () =>
        {
            var report = new Report(
                Id: 0, // Assigned by database
                MessageId: (int)messageId,
                ChatId: chatId,
                ReportCommandMessageId: null,
                ReportedByUserId: null,
                ReportedByUserName: "File Scanner",
                ReportedAt: DateTimeOffset.UtcNow,
                Status: ReportStatus.Pending,
                ReviewedBy: null,
                ReviewedAt: null,
                ActionTaken: null,
                AdminNotes: $"MALWARE DETECTED: {malwareDetails}\n\nUser was NOT auto-banned (malware upload may be accidental).",
                WebUserId: null);

            await _reportService.CreateReportAsync(report, telegramMessage, isAutomated: true, cancellationToken);
        }, "Create malware report");

        // Step 4: Notify admins via system notification
        await SafeExecuteAsync(async () =>
        {
            var chatName = chat?.ChatName ?? chatId.ToString();
            var userName = user != null
                ? TelegramDisplayName.Format(user.FirstName, user.LastName, user.Username, userId)
                : userId.ToString();

            await _notificationService.SendSystemNotificationAsync(
                NotificationEventType.MalwareDetected,
                "Malware Detected and Removed",
                $"Malware was detected in chat '{chatName}' and the message was deleted.\n\n" +
                $"User: {userName}\n" +
                $"Detection: {malwareDetails}\n\n" +
                $"The user was NOT auto-banned (malware upload may be accidental). Please review the report in the admin panel.",
                cancellationToken);
        }, "Malware notification");

        _logger.LogInformation(
            "Malware violation handled: deleted message {MessageId}, created report, notified admins (no ban)",
            messageId);

        return new ModerationResult
        {
            Success = true,
            MessageDeleted = deleteResult.MessageDeleted
        };
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> HandleCriticalViolationAsync(
        long messageId,
        long chatId,
        long userId,
        List<string> violations,
        Message? telegramMessage = null,
        CancellationToken cancellationToken = default)
    {
        // Fetch once for logging
        var user = await _userRepository.GetByTelegramIdAsync(userId, cancellationToken);
        var chat = await _managedChatsRepository.GetByChatIdAsync(chatId, cancellationToken);

        _logger.LogWarning(
            "Handling critical violation for message {MessageId} from {User} in {Chat}: {Violations}",
            messageId, user.ToLogDebug(userId), chat.ToLogDebug(chatId), string.Join("; ", violations));

        // Step 1: Delete the violating message
        var deleteResult = await _messageHandler.DeleteAsync(chatId, messageId, Actor.AutoDetection, cancellationToken);

        await SafeAuditAsync(
            () => _auditHandler.LogDeleteAsync(messageId, chatId, userId, Actor.AutoDetection, cancellationToken),
            "critical violation deletion", userId, chatId);

        // Step 2: Notify user via DM (trusted users get an explanation, not a ban)
        await SafeExecuteAsync(
            () => _notificationHandler.NotifyUserCriticalViolationAsync(userId, violations, cancellationToken),
            $"Critical violation notification for user {userId}");

        _logger.LogInformation(
            "Critical violation handled: deleted message {MessageId}, notified user (no ban/warning for trusted user)",
            messageId);

        return new ModerationResult
        {
            Success = true,
            MessageDeleted = deleteResult.MessageDeleted
        };
    }

    /// <summary>
    /// Checks if user is a Telegram system account (777000, 1087968824, etc.) and returns error if moderation is attempted.
    /// </summary>
    private async Task<ModerationResult?> CheckServiceAccountProtectionAsync(long userId, CancellationToken cancellationToken)
    {
        if (TelegramConstants.IsSystemUser(userId))
        {
            var user = await _userRepository.GetByTelegramIdAsync(userId, cancellationToken);
            _logger.LogWarning(
                "Moderation action blocked for Telegram system account ({User})",
                user.ToLogDebug(userId));

            return ModerationResult.SystemAccountBlocked();
        }

        return null;
    }

    /// <summary>
    /// Safely executes an audit operation, logging any failures without blocking the main operation.
    /// Telegram operations are the primary job; audit/tracking is secondary.
    /// </summary>
    private async Task SafeAuditAsync(Func<Task> auditAction, string operationName, long userId, long? chatId = null)
    {
        try
        {
            await auditAction();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to audit {Operation} (user: {UserId}, chat: {ChatId}) - Telegram operation succeeded",
                operationName, userId, chatId);
        }
    }

    /// <summary>
    /// Executes a non-critical operation, logging and swallowing any exceptions.
    /// Use for operations that shouldn't fail the primary workflow (e.g., training data, cleanup scheduling).
    /// </summary>
    private async Task SafeExecuteAsync(Func<Task> action, string operationName)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Operation} failed, continuing", operationName);
        }
    }
}
