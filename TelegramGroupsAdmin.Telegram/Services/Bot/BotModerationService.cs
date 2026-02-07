using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
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
        _banCelebrationService = banCelebrationService;
        _reportService = reportService;
        _notificationService = notificationService;
        _configService = configService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> MarkAsSpamAndBanAsync(
        SpamBanIntent intent,
        CancellationToken cancellationToken = default)
    {
        var protectionResult = CheckServiceAccountProtection(intent.User);
        if (protectionResult != null) return protectionResult;

        // Step 1: Ensure message exists in database (backfill if needed for training data)
        await _messageHandler.EnsureExistsAsync(intent.MessageId, intent.Chat, intent.TelegramMessage, cancellationToken);

        // Step 2: Delete message (best effort - may already be deleted)
        var deleteResult = await _messageHandler.DeleteAsync(intent.Chat, intent.MessageId, intent.Executor, cancellationToken);
        if (deleteResult.MessageDeleted)
        {
            await SafeAuditAsync(
                () => _auditHandler.LogDeleteAsync(intent.MessageId, intent.Chat, intent.User, intent.Executor, cancellationToken),
                "message deletion", intent.User, intent.Chat);
        }

        // Step 3: Ban user globally (inline - don't call BanUserAsync to control notification)
        var banResult = await _banHandler.BanAsync(intent.User, intent.Executor, intent.Reason, intent.MessageId, cancellationToken);

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
            () => _auditHandler.LogBanAsync(intent.User, intent.Executor, intent.Reason, cancellationToken),
            "ban", intent.User);

        // Business rule: Bans always revoke trust
        var trustRevoked = await RevokeTrustOnBanAsync(intent.User, intent.Executor, intent.Reason, cancellationToken);

        // Schedule cleanup of user's messages
        await SafeExecuteAsync(
            () => _messageHandler.ScheduleUserMessagesCleanupAsync(intent.User.Id, cancellationToken),
            $"Schedule messages cleanup for user {intent.User.Id}");

        // Step 4: Create training data (non-critical - failure doesn't affect ban success)
        await SafeExecuteAsync(
            () => _trainingHandler.CreateSpamSampleAsync(intent.MessageId, intent.Executor, cancellationToken),
            $"Create training data for message {intent.MessageId}");

        // Step 5: Send ban celebration (non-critical - failure doesn't affect ban success)
        await SafeExecuteAsync(
            async () =>
            {
                await _banCelebrationService.SendBanCelebrationAsync(
                    intent.Chat.Id,
                    intent.Chat.ChatName ?? intent.Chat.Id.ToString(),
                    intent.User.Id,
                    intent.User.DisplayName,
                    isAutoBan: intent.Executor.Type is ActorType.System, cancellationToken);
            },
            $"Send ban celebration for user {intent.User.Id} in chat {intent.Chat.Id}");

        // Step 6: Rich admin notification (replaces simple notification from BanUserAsync)
        await SafeExecuteAsync(
            async () =>
            {
                var enrichedMessage = await _messageHandler.GetEnrichedAsync(intent.MessageId, cancellationToken);
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
                    await _notificationHandler.NotifyAdminsBanAsync(intent.User, intent.Executor, intent.Reason, cancellationToken);
                }
            },
            $"Rich spam notification for user {intent.User.Id}");

        return new ModerationResult
        {
            Success = true,
            ChatsAffected = banResult.ChatsAffected,
            MessageDeleted = deleteResult.MessageDeleted,
            TrustRemoved = trustRevoked
        };
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> BanUserAsync(
        BanIntent intent,
        CancellationToken cancellationToken = default)
    {
        var protectionResult = CheckServiceAccountProtection(intent.User);
        if (protectionResult != null) return protectionResult;

        // Primary action: Ban globally
        var banResult = await _banHandler.BanAsync(intent.User, intent.Executor, intent.Reason, intent.MessageId, cancellationToken);

        if (!banResult.Success)
            return ModerationResult.Failed(banResult.ErrorMessage ?? "Ban failed");

        // Audit successful ban (separate from BanHandler's state tracking record)
        await SafeAuditAsync(
            () => _auditHandler.LogBanAsync(intent.User, intent.Executor, intent.Reason, cancellationToken),
            "ban", intent.User);

        // Business rule: Bans always revoke trust
        var trustRevoked = await RevokeTrustOnBanAsync(intent.User, intent.Executor, intent.Reason, cancellationToken);

        // Notify admins
        await SafeExecuteAsync(
            () => _notificationHandler.NotifyAdminsBanAsync(intent.User, intent.Executor, intent.Reason, cancellationToken),
            $"Ban notification for user {intent.User.Id}");

        // Schedule cleanup of user's messages (non-critical - don't fail the ban if this fails)
        await SafeExecuteAsync(
            () => _messageHandler.ScheduleUserMessagesCleanupAsync(intent.User.Id, cancellationToken),
            $"Schedule messages cleanup for user {intent.User.Id}");

        // Bug 3 fix: Ban celebration when chat context is provided
        // (enables celebrations for CAS/Impersonation bans that carry the originating chat)
        if (intent.Chat is { } celebrationChat)
        {
            await SafeExecuteAsync(async () =>
            {
                await _banCelebrationService.SendBanCelebrationAsync(
                    celebrationChat.Id,
                    celebrationChat.ChatName ?? celebrationChat.Id.ToString(),
                    intent.User.Id,
                    intent.User.DisplayName,
                    isAutoBan: intent.Executor.Type is ActorType.System, cancellationToken);
            }, $"Send ban celebration for user {intent.User.Id} in chat {celebrationChat.Id}");
        }

        return new ModerationResult
        {
            Success = true,
            ChatsAffected = banResult.ChatsAffected,
            TrustRemoved = trustRevoked
        };
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> WarnUserAsync(
        WarnIntent intent,
        CancellationToken cancellationToken = default)
    {
        var protectionResult = CheckServiceAccountProtection(intent.User);
        if (protectionResult != null) return protectionResult;

        // Primary action: Issue warning (writes to warnings table)
        var warnResult = await _warnHandler.WarnAsync(intent.User, intent.Executor, intent.Reason, intent.Chat.Id, intent.MessageId, cancellationToken);

        if (!warnResult.Success)
            return new ModerationResult { Success = false, ErrorMessage = warnResult.ErrorMessage };

        // Audit successful warning
        await SafeAuditAsync(
            () => _auditHandler.LogWarnAsync(intent.User, intent.Executor, intent.Reason, cancellationToken),
            "warning", intent.User, intent.Chat);

        // Notify user about warning
        await SafeExecuteAsync(
            () => _notificationHandler.NotifyUserWarningAsync(intent.User, warnResult.WarningCount, intent.Reason, cancellationToken),
            $"Warning notification for user {intent.User.Id}");

        var result = new ModerationResult
        {
            Success = true,
            WarningCount = warnResult.WarningCount
        };

        // Business rule: Check warning threshold for auto-ban
        var warningConfig = await _configService.GetEffectiveAsync<WarningSystemConfig>(
            ConfigType.Moderation, intent.Chat.Id) ?? WarningSystemConfig.Default;

        if (warningConfig.AutoBanEnabled &&
            warningConfig.AutoBanThreshold > 0 &&
            warnResult.WarningCount >= warningConfig.AutoBanThreshold)
        {
            _logger.LogWarning(
                "Auto-ban triggered: {User} has {WarnCount} warnings (threshold: {Threshold})",
                intent.User.ToLogDebug(), warnResult.WarningCount, warningConfig.AutoBanThreshold);

            // Use configured auto-ban reason with {count} placeholder support
            var autoBanReason = !string.IsNullOrWhiteSpace(warningConfig.AutoBanReason)
                ? warningConfig.AutoBanReason.Replace("{count}", warnResult.WarningCount.ToString())
                : $"Exceeded warning threshold ({warnResult.WarningCount}/{warningConfig.AutoBanThreshold} warnings)";

            // Auto-ban: Call handlers directly (don't call BanUserAsync to avoid nested orchestrator calls)
            var banResult = await _banHandler.BanAsync(intent.User, Actor.AutoBan, autoBanReason, intent.MessageId, cancellationToken);

            if (banResult.Success)
            {
                // Audit successful auto-ban
                await SafeAuditAsync(
                    () => _auditHandler.LogBanAsync(intent.User, Actor.AutoBan, autoBanReason, cancellationToken),
                    "auto-ban (from warnings)", intent.User, intent.Chat);

                // Business rule: Bans always revoke trust
                var trustRevoked = await RevokeTrustOnBanAsync(intent.User, Actor.AutoBan, autoBanReason, cancellationToken);

                // Notify admins (simple notification - no detection context for warning-based bans)
                await SafeExecuteAsync(
                    () => _notificationHandler.NotifyAdminsBanAsync(intent.User, Actor.AutoBan, autoBanReason, cancellationToken),
                    $"Auto-ban notification for user {intent.User.Id}");

                // Schedule cleanup of user's messages
                await SafeExecuteAsync(
                    () => _messageHandler.ScheduleUserMessagesCleanupAsync(intent.User.Id, cancellationToken),
                    $"Schedule messages cleanup for user {intent.User.Id}");

                result = result with
                {
                    AutoBanTriggered = true,
                    ChatsAffected = banResult.ChatsAffected,
                    TrustRemoved = trustRevoked
                };
            }
            else
            {
                _logger.LogError(
                    "Auto-ban failed for {User}: {Error}",
                    intent.User.ToLogDebug(), banResult.ErrorMessage);
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> TrustUserAsync(
        TrustIntent intent,
        CancellationToken cancellationToken = default)
    {
        var trustResult = await _trustHandler.TrustAsync(intent.User, intent.Executor, intent.Reason, cancellationToken);

        if (!trustResult.Success)
            return new ModerationResult { Success = false, ErrorMessage = trustResult.ErrorMessage };

        // Audit successful trust
        await SafeAuditAsync(
            () => _auditHandler.LogTrustAsync(intent.User, intent.Executor, intent.Reason, cancellationToken),
            "trust", intent.User);

        return new ModerationResult { Success = true };
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> UntrustUserAsync(
        UntrustIntent intent,
        CancellationToken cancellationToken = default)
    {
        var untrustResult = await _trustHandler.UntrustAsync(intent.User, intent.Executor, intent.Reason, cancellationToken);

        if (!untrustResult.Success)
            return new ModerationResult { Success = false, ErrorMessage = untrustResult.ErrorMessage };

        // Audit successful untrust
        await SafeAuditAsync(
            () => _auditHandler.LogUntrustAsync(intent.User, intent.Executor, intent.Reason, cancellationToken),
            "untrust", intent.User);

        return new ModerationResult { Success = true };
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> UnbanUserAsync(
        UnbanIntent intent,
        CancellationToken cancellationToken = default)
    {
        var unbanResult = await _banHandler.UnbanAsync(intent.User, intent.Executor, intent.Reason, cancellationToken);

        if (!unbanResult.Success)
            return new ModerationResult { Success = false, ErrorMessage = unbanResult.ErrorMessage };

        // Audit successful unban
        await SafeAuditAsync(
            () => _auditHandler.LogUnbanAsync(intent.User, intent.Executor, intent.Reason, cancellationToken),
            "unban", intent.User);

        var result = new ModerationResult
        {
            Success = true,
            ChatsAffected = unbanResult.ChatsAffected
        };

        // Handle trust restoration as follow-up
        if (intent.RestoreTrust)
        {
            var trustResult = await TrustUserAsync(
                new TrustIntent
                {
                    User = intent.User,
                    Executor = intent.Executor,
                    Reason = "Trust restored after unban (false positive correction)"
                }, cancellationToken);
            result = result with { TrustRestored = trustResult.Success };
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> DeleteMessageAsync(
        DeleteMessageIntent intent,
        CancellationToken cancellationToken = default)
    {
        var deleteResult = await _messageHandler.DeleteAsync(intent.Chat, intent.MessageId, intent.Executor, cancellationToken);

        // Audit the deletion attempt (even if message was already deleted)
        await SafeAuditAsync(
            () => _auditHandler.LogDeleteAsync(intent.MessageId, intent.Chat, intent.User, intent.Executor, cancellationToken),
            "message deletion", intent.User, intent.Chat);

        return new ModerationResult
        {
            Success = true,
            MessageDeleted = deleteResult.MessageDeleted
        };
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> TempBanUserAsync(
        TempBanIntent intent,
        CancellationToken cancellationToken = default)
    {
        var protectionResult = CheckServiceAccountProtection(intent.User);
        if (protectionResult != null) return protectionResult;

        var tempBanResult = await _banHandler.TempBanAsync(intent.User, intent.Executor, intent.Duration, intent.Reason, intent.MessageId, cancellationToken);

        if (!tempBanResult.Success)
            return new ModerationResult { Success = false, ErrorMessage = tempBanResult.ErrorMessage };

        // Audit successful temp ban (separate from BanHandler's state tracking record)
        await SafeAuditAsync(
            () => _auditHandler.LogTempBanAsync(intent.User, intent.Executor, intent.Duration, intent.Reason, cancellationToken),
            "temp ban", intent.User);

        // Notify user about temp ban with rejoin info
        await SafeExecuteAsync(
            () => _notificationHandler.NotifyUserTempBanAsync(intent.User, intent.Duration, tempBanResult.ExpiresAt, intent.Reason, cancellationToken),
            $"Temp-ban notification for user {intent.User.Id}");

        return new ModerationResult
        {
            Success = true,
            ChatsAffected = tempBanResult.ChatsAffected
        };
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> RestrictUserAsync(
        RestrictIntent intent,
        CancellationToken cancellationToken = default)
    {
        var protectionResult = CheckServiceAccountProtection(intent.User);
        if (protectionResult != null) return protectionResult;

        var restrictResult = await _restrictHandler.RestrictAsync(
            intent.User, intent.Chat, intent.Executor, intent.Duration, intent.Reason, cancellationToken);

        if (!restrictResult.Success)
            return new ModerationResult { Success = false, ErrorMessage = restrictResult.ErrorMessage };

        // Audit successful restriction
        await SafeAuditAsync(
            () => _auditHandler.LogRestrictAsync(intent.User, intent.Chat, intent.Executor, intent.Reason, cancellationToken),
            "restriction", intent.User, intent.Chat);

        return new ModerationResult
        {
            Success = true,
            ChatsAffected = restrictResult.ChatsAffected
        };
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> SyncBanToChatAsync(
        SyncBanIntent intent,
        CancellationToken cancellationToken = default)
    {
        var protectionResult = CheckServiceAccountProtection(intent.User);
        if (protectionResult != null) return protectionResult;

        var banResult = await _banHandler.BanInChatAsync(
            intent.User, intent.Chat, intent.Executor, intent.Reason, intent.TriggeredByMessageId, cancellationToken);

        if (!banResult.Success)
            return ModerationResult.Failed(banResult.ErrorMessage ?? "Ban sync failed");

        // Audit successful ban sync
        await SafeAuditAsync(
            () => _auditHandler.LogBanAsync(intent.User, intent.Executor, intent.Reason, cancellationToken),
            "ban sync", intent.User, intent.Chat);

        return new ModerationResult
        {
            Success = true,
            ChatsAffected = banResult.ChatsAffected
        };
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> RestoreUserPermissionsAsync(
        RestorePermissionsIntent intent,
        CancellationToken cancellationToken = default)
    {
        var protectionResult = CheckServiceAccountProtection(intent.User);
        if (protectionResult != null) return protectionResult;

        var restrictResult = await _restrictHandler.RestorePermissionsAsync(
            intent.User, intent.Chat, intent.Executor, intent.Reason, cancellationToken);

        if (!restrictResult.Success)
            return ModerationResult.Failed(restrictResult.ErrorMessage ?? "Failed to restore permissions");

        // Audit successful permission restoration
        await SafeAuditAsync(
            () => _auditHandler.LogRestorePermissionsAsync(intent.User, intent.Chat, intent.Executor, intent.Reason, cancellationToken),
            "restore permissions", intent.User, intent.Chat);

        return new ModerationResult
        {
            Success = true,
            ChatsAffected = restrictResult.ChatsAffected
        };
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> KickUserFromChatAsync(
        KickIntent intent,
        CancellationToken cancellationToken = default)
    {
        var protectionResult = CheckServiceAccountProtection(intent.User);
        if (protectionResult != null) return protectionResult;

        var kickResult = await _banHandler.KickFromChatAsync(
            intent.User, intent.Chat, intent.Executor, intent.Reason, cancellationToken);

        if (!kickResult.Success)
            return ModerationResult.Failed(kickResult.ErrorMessage ?? "Failed to kick user");

        // Audit successful kick
        await SafeAuditAsync(
            () => _auditHandler.LogKickAsync(intent.User, intent.Chat, intent.Executor, intent.Reason, cancellationToken),
            "kick", intent.User, intent.Chat);

        return new ModerationResult
        {
            Success = true,
            ChatsAffected = kickResult.ChatsAffected
        };
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> HandleMalwareViolationAsync(
        MalwareViolationIntent intent,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "Handling malware violation for message {MessageId} from {User} in {Chat}: {Details}",
            intent.MessageId, intent.User.ToLogDebug(), intent.Chat.ToLogDebug(), intent.MalwareDetails);

        // Step 1: Ensure message exists in database (for audit trail)
        await _messageHandler.EnsureExistsAsync(intent.MessageId, intent.Chat, intent.TelegramMessage, cancellationToken);

        // Step 2: Delete the malware message
        var deleteResult = await _messageHandler.DeleteAsync(intent.Chat, intent.MessageId, Actor.FileScanner, cancellationToken);

        await SafeAuditAsync(
            () => _auditHandler.LogDeleteAsync(intent.MessageId, intent.Chat, intent.User, Actor.FileScanner, cancellationToken),
            "malware deletion", intent.User, intent.Chat);

        // Step 3: Create admin report for review
        await SafeExecuteAsync(async () =>
        {
            var report = new Report(
                Id: 0, // Assigned by database
                MessageId: (int)intent.MessageId,
                ChatId: intent.Chat.Id,
                ReportCommandMessageId: null,
                ReportedByUserId: null,
                ReportedByUserName: "File Scanner",
                ReportedAt: DateTimeOffset.UtcNow,
                Status: ReportStatus.Pending,
                ReviewedBy: null,
                ReviewedAt: null,
                ActionTaken: null,
                AdminNotes: $"MALWARE DETECTED: {intent.MalwareDetails}\n\nUser was NOT auto-banned (malware upload may be accidental).",
                WebUserId: null);

            await _reportService.CreateReportAsync(report, intent.TelegramMessage, isAutomated: true, cancellationToken);
        }, "Create malware report");

        // Step 4: Notify admins via system notification
        await SafeExecuteAsync(async () =>
        {
            var chatName = intent.Chat.ChatName ?? intent.Chat.Id.ToString();

            await _notificationService.SendSystemNotificationAsync(
                NotificationEventType.MalwareDetected,
                "Malware Detected and Removed",
                $"Malware was detected in chat '{chatName}' and the message was deleted.\n\n" +
                $"User: {intent.User.DisplayName}\n" +
                $"Detection: {intent.MalwareDetails}\n\n" +
                $"The user was NOT auto-banned (malware upload may be accidental). Please review the report in the admin panel.",
                cancellationToken);
        }, "Malware notification");

        _logger.LogInformation(
            "Malware violation handled: deleted message {MessageId}, created report, notified admins (no ban)",
            intent.MessageId);

        return new ModerationResult
        {
            Success = true,
            MessageDeleted = deleteResult.MessageDeleted
        };
    }

    /// <inheritdoc/>
    public async Task<ModerationResult> HandleCriticalViolationAsync(
        CriticalViolationIntent intent,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "Handling critical violation for message {MessageId} from {User} in {Chat}: {Violations}",
            intent.MessageId, intent.User.ToLogDebug(), intent.Chat.ToLogDebug(), string.Join("; ", intent.Violations));

        // Step 1: Delete the violating message
        var deleteResult = await _messageHandler.DeleteAsync(intent.Chat, intent.MessageId, Actor.AutoDetection, cancellationToken);

        await SafeAuditAsync(
            () => _auditHandler.LogDeleteAsync(intent.MessageId, intent.Chat, intent.User, Actor.AutoDetection, cancellationToken),
            "critical violation deletion", intent.User, intent.Chat);

        // Step 2: Notify user via DM (trusted users get an explanation, not a ban)
        await SafeExecuteAsync(
            () => _notificationHandler.NotifyUserCriticalViolationAsync(intent.User, intent.Violations, cancellationToken),
            $"Critical violation notification for user {intent.User.Id}");

        _logger.LogInformation(
            "Critical violation handled: deleted message {MessageId}, notified user (no ban/warning for trusted user)",
            intent.MessageId);

        return new ModerationResult
        {
            Success = true,
            MessageDeleted = deleteResult.MessageDeleted
        };
    }

    /// <summary>
    /// Checks if user is a Telegram system account (777000, 1087968824, etc.) and returns error if moderation is attempted.
    /// </summary>
    private ModerationResult? CheckServiceAccountProtection(UserIdentity user)
    {
        if (TelegramConstants.IsSystemUser(user.Id))
        {
            _logger.LogWarning(
                "Moderation action blocked for Telegram system account ({User})",
                user.ToLogDebug());

            return ModerationResult.SystemAccountBlocked();
        }

        return null;
    }

    /// <summary>
    /// Business rule: Bans always revoke trust. Handles the untrust call and its audit entry.
    /// Returns true if trust was successfully revoked.
    /// </summary>
    private async Task<bool> RevokeTrustOnBanAsync(
        UserIdentity user, Actor executor, string? banReason, CancellationToken cancellationToken)
    {
        var untrustReason = string.IsNullOrWhiteSpace(banReason)
            ? "Trust revoked due to ban"
            : $"Trust revoked due to ban: {banReason}";
        var untrustResult = await _trustHandler.UntrustAsync(user, executor, untrustReason, cancellationToken);

        if (untrustResult.Success)
        {
            await SafeAuditAsync(
                () => _auditHandler.LogUntrustAsync(user, executor, untrustReason, cancellationToken),
                "untrust (from ban)", user);
        }

        return untrustResult.Success;
    }

    /// <summary>
    /// Safely executes an audit operation, logging any failures without blocking the main operation.
    /// Telegram operations are the primary job; audit/tracking is secondary.
    /// </summary>
    private async Task SafeAuditAsync(Func<Task> auditAction, string operationName, UserIdentity user, ChatIdentity? chat = null)
    {
        try
        {
            await auditAction();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to audit {Operation} (user: {User}, chat: {Chat}) - Telegram operation succeeded",
                operationName, user.ToLogDebug(), chat?.ToLogDebug());
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
