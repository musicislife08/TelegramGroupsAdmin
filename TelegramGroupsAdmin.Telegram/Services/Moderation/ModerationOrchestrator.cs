using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Core;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Handlers;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation;

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
public class ModerationOrchestrator : IModerationOrchestrator
{
    // Domain handlers (workers)
    private readonly IBanHandler _banHandler;
    private readonly ITrustHandler _trustHandler;
    private readonly IWarnHandler _warnHandler;
    private readonly IMessageHandler _messageHandler;
    private readonly IRestrictHandler _restrictHandler;

    // Support handlers
    private readonly IAuditHandler _auditHandler;
    private readonly INotificationHandler _notificationHandler;
    private readonly ITrainingHandler _trainingHandler;

    // Repositories for logging
    private readonly ITelegramUserRepository _userRepository;
    private readonly IManagedChatsRepository _managedChatsRepository;

    // Services
    private readonly IBanCelebrationService _banCelebrationService;

    // Configuration
    private readonly IConfigService _configService;
    private readonly ILogger<ModerationOrchestrator> _logger;

    public ModerationOrchestrator(
        IBanHandler banHandler,
        ITrustHandler trustHandler,
        IWarnHandler warnHandler,
        IMessageHandler messageHandler,
        IRestrictHandler restrictHandler,
        IAuditHandler auditHandler,
        INotificationHandler notificationHandler,
        ITrainingHandler trainingHandler,
        ITelegramUserRepository userRepository,
        IManagedChatsRepository managedChatsRepository,
        IBanCelebrationService banCelebrationService,
        IConfigService configService,
        ILogger<ModerationOrchestrator> logger)
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
            await _auditHandler.LogDeleteAsync(messageId, chatId, userId, executor, cancellationToken);
        }

        // Step 3: Ban user (self-call - reuses ban logic including trust revocation)
        var banResult = await BanUserAsync(userId, messageId, executor, reason, cancellationToken);

        if (!banResult.Success)
        {
            return new ModerationResult
            {
                Success = false,
                ErrorMessage = banResult.ErrorMessage,
                MessageDeleted = deleteResult.MessageDeleted
            };
        }

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

        return new ModerationResult
        {
            Success = true,
            ChatsAffected = banResult.ChatsAffected,
            MessageDeleted = deleteResult.MessageDeleted,
            TrustRemoved = banResult.TrustRemoved
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
        await _auditHandler.LogBanAsync(userId, executor, reason, cancellationToken);

        // Business rule: Bans always revoke trust
        var untrustReason = string.IsNullOrWhiteSpace(reason)
            ? "Trust revoked due to ban"
            : $"Trust revoked due to ban: {reason}";
        var untrustResult = await _trustHandler.UntrustAsync(
            userId, executor, untrustReason, cancellationToken);

        if (untrustResult.Success)
        {
            await _auditHandler.LogUntrustAsync(userId, executor, untrustReason, cancellationToken);
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
        await _auditHandler.LogWarnAsync(userId, executor, reason, cancellationToken);

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
            var banResult = await BanUserAsync(userId, messageId, Actor.AutoBan, autoBanReason, cancellationToken);

            if (banResult.Success)
            {
                result = result with
                {
                    AutoBanTriggered = true,
                    ChatsAffected = banResult.ChatsAffected,
                    TrustRemoved = banResult.TrustRemoved
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
        await _auditHandler.LogTrustAsync(userId, executor, reason, cancellationToken);

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
        await _auditHandler.LogUntrustAsync(userId, executor, reason, cancellationToken);

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
        await _auditHandler.LogUnbanAsync(userId, executor, reason, cancellationToken);

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
        await _auditHandler.LogDeleteAsync(messageId, chatId, userId, deletedBy, cancellationToken);

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
        await _auditHandler.LogTempBanAsync(userId, executor, duration, reason, cancellationToken);

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
        await _auditHandler.LogRestrictAsync(userId, chatId ?? 0, executor, reason, cancellationToken);

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
        await _auditHandler.LogBanAsync(user.Id, Actor.AutoDetection, reason, cancellationToken);

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
        await _auditHandler.LogRestorePermissionsAsync(userId, chatId, executor, reason, cancellationToken);

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
        await _auditHandler.LogKickAsync(userId, chatId, executor, reason, cancellationToken);

        return new ModerationResult
        {
            Success = true,
            ChatsAffected = kickResult.ChatsAffected
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
